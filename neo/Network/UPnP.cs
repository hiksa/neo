using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace Neo.Network
{
    public class UPnP
    {
        private static string serviceUrl;

        public static TimeSpan TimeOut { get; set; } = TimeSpan.FromSeconds(3);

        public static bool Discover()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.ReceiveTimeout = (int)TimeOut.TotalMilliseconds;
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

                var request = "M-SEARCH * HTTP/1.1\r\n" +
                    "HOST: 239.255.255.250:1900\r\n" +
                    "ST:upnp:rootdevice\r\n" +
                    "MAN:\"ssdp:discover\"\r\n" +
                    "MX:3\r\n\r\n";

                var data = Encoding.ASCII.GetBytes(request);
                var endpoint = new IPEndPoint(IPAddress.Broadcast, 1900);
                var start = DateTime.Now;

                try
                {
                    socket.SendTo(data, endpoint);
                    socket.SendTo(data, endpoint);
                    socket.SendTo(data, endpoint);
                }
                catch
                {
                    return false;
                }

                var buffer = new byte[0x1000];

                do
                {
                    int length;
                    try
                    {
                        length = socket.Receive(buffer);

                        var response = Encoding.ASCII.GetString(buffer, 0, length).ToLower();
                        if (response.Contains("upnp:rootdevice"))
                        {
                            var indexOfLocationEnd = response.ToLower().IndexOf("location:") + "location:".Length;
                            response = response.Substring(indexOfLocationEnd);

                            var uri = response.Substring(0, response.IndexOf("\r")).Trim();
                            serviceUrl = UPnP.GetServiceUrl(uri);

                            if (!string.IsNullOrEmpty(serviceUrl))
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                while (DateTime.Now - start < UPnP.TimeOut);

                return false;
            }
        }

        public static void ForwardPort(int port, ProtocolType protocol, string description)
        {
            if (string.IsNullOrEmpty(serviceUrl))
            {
                throw new Exception("No UPnP service available or Discover() has not been called");
            }

            var soap = 
                "<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
                    "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port.ToString() + "</NewExternalPort><NewProtocol>" + protocol.ToString().ToUpper() + "</NewProtocol>" +
                    "<NewInternalPort>" + port.ToString() + "</NewInternalPort><NewInternalClient>" + Dns.GetHostAddresses(Dns.GetHostName()).First(p => p.AddressFamily == AddressFamily.InterNetwork).ToString() +
                    "</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>" + description +
                "</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration></u:AddPortMapping>";

            var xdoc = UPnP.SOAPRequest(serviceUrl, soap, "AddPortMapping");
        }

        public static void DeleteForwardingRule(int port, ProtocolType protocol)
        {
            if (string.IsNullOrEmpty(serviceUrl))
            {
                throw new Exception("No UPnP service available or Discover() has not been called");
            }

            string soap =
                "<u:DeletePortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
                    "<NewRemoteHost>" + "</NewRemoteHost>" +
                    "<NewExternalPort>" + port + "</NewExternalPort>" +
                    "<NewProtocol>" + protocol.ToString().ToUpper() + "</NewProtocol>" +
                "</u:DeletePortMapping>";

            var xdoc = UPnP.SOAPRequest(serviceUrl, soap, "DeletePortMapping");
        }

        public static IPAddress GetExternalIP()
        {
            if (string.IsNullOrEmpty(serviceUrl))
            {
                throw new Exception("No UPnP service available or Discover() has not been called");
            }

            var soap = "<u:GetExternalIPAddress xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\"></u:GetExternalIPAddress>";
            var soapRequest = UPnP.SOAPRequest(serviceUrl, soap, "GetExternalIPAddress");
            var namespaceManager = new XmlNamespaceManager(soapRequest.NameTable);
            namespaceManager.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");

            var rawIp = soapRequest.SelectSingleNode("//NewExternalIPAddress/text()", namespaceManager).Value;
            var ip = IPAddress.Parse(rawIp);
            return ip;
        }

        private static string GetServiceUrl(string resp)
        {
            try
            {
                var desc = new XmlDocument();
                var webRequest = WebRequest.CreateHttp(resp);
                using (var webResponse = webRequest.GetResponse())
                {
                    desc.Load(webResponse.GetResponseStream());
                }

                var namespaceManager = new XmlNamespaceManager(desc.NameTable);
                namespaceManager.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");

                var deviceTypeXmlNode = desc.SelectSingleNode("//tns:device/tns:deviceType/text()", namespaceManager);
                if (!deviceTypeXmlNode.Value.Contains("InternetGatewayDevice"))
                {
                    return null;
                }

                var serviceTypeXmlNode = desc.SelectSingleNode("//tns:service[contains(tns:serviceType,\"WANIPConnection\")]/tns:controlURL/text()", namespaceManager);
                if (serviceTypeXmlNode == null)
                {
                    return null;
                }

                var eventXmlNode = desc.SelectSingleNode("//tns:service[contains(tns:serviceType,\"WANIPConnection\")]/tns:eventSubURL/text()", namespaceManager);
                return CombineUrls(resp, serviceTypeXmlNode.Value);
            }
            catch
            {
                return null;
            }
        }

        private static string CombineUrls(string resp, string urlSecondPart)
        {
            var startIndex = resp.IndexOf("://") + 3;
            var length = resp.IndexOf('/', startIndex);

            return resp.Substring(0, length) + urlSecondPart;
        }

        private static XmlDocument SOAPRequest(string url, string soap, string function)
        {
            var requestContent = "<?xml version=\"1.0\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" + soap + "</s:Body>" +
            "</s:Envelope>";

            var request = WebRequest.CreateHttp(url);
            request.Method = "POST";

            var requestContentBytes = Encoding.UTF8.GetBytes(requestContent);
            request.Headers["SOAPACTION"] = "\"urn:schemas-upnp-org:service:WANIPConnection:1#" + function + "\"";
            request.ContentType = "text/xml; charset=\"utf-8\"";

            using (var reqs = request.GetRequestStream())
            {
                reqs.Write(requestContentBytes, 0, requestContentBytes.Length);
                var result = new XmlDocument();
                var webResponse = request.GetResponse();
                using (var responseStream = webResponse.GetResponseStream())
                {
                    result.Load(responseStream);
                    return result;
                }
            }
        }
    }
}
