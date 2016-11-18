using System;
using System.Net;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace EOBrowser
{
	public class ProxyController
	{
		private ProxyServer proxyServer;

		public ProxyController()
		{
			proxyServer = new ProxyServer();
			proxyServer.TrustRootCertificate = false;
			// proxyServer.certValidated = false;
		}

		public bool started = false;

		public void StartProxy(int listen_port, string upstream_proxy_host, int upstream_proxy_port)
		{
			proxyServer.BeforeRequest += OnRequest;
			proxyServer.BeforeResponse += OnResponse;
			proxyServer.AddEndPoint(new ExplicitProxyEndPoint(IPAddress.Parse("127.0.0.1"), listen_port, true));
			if (!String.IsNullOrEmpty(upstream_proxy_host) && upstream_proxy_port != 0) {
				proxyServer.UpStreamHttpProxy = new ExternalProxy() { HostName = upstream_proxy_host, Port = upstream_proxy_port };
			}
			proxyServer.Start();
			foreach (var endPoint in proxyServer.ProxyEndPoints)
				Console.WriteLine("Listening on '{0}' endpoint at Ip {1} and port: {2} ",
					endPoint.GetType().Name, endPoint.IpAddress, endPoint.Port);
			started = true;
		}

		public void Stop()
		{
			proxyServer.BeforeRequest -= OnRequest;
			proxyServer.BeforeResponse -= OnResponse;
			proxyServer.Stop();
		}

		public async Task OnRequest(object sender, SessionEventArgs e)
		{
			// Console.WriteLine(e.WebSession.Request.RequestUri.AbsoluteUri);

			////read request headers
			var requestHeaders = e.WebSession.Request.RequestHeaders;

			var method = e.WebSession.Request.Method.ToUpper();
			if ((method == "POST" || method == "PUT" || method == "PATCH"))
			{
				//Get/Set request body bytes
				byte[] bodyBytes = await e.GetRequestBody();
				await e.SetRequestBody(bodyBytes);

				//Get/Set request body as string
				string bodyString = await e.GetRequestBodyAsString();
				await e.SetRequestBodyString(bodyString);

			}

			//To cancel a request with a custom HTML content
			//Filter URL
			if (e.WebSession.Request.RequestUri.AbsoluteUri.Contains("http://www.bing.com/s/a/hp_zh_cn.png"))
			{
				await e.Ok("<!DOCTYPE html>" +
					  "<html><body><h1>" +
					  "Website Blocked" +
					  "</h1>" +
					  "<p>Blocked by titanium web proxy.</p>" +
					  "</body>" +
					  "</html>");
			}
			//Redirect example
			if (e.WebSession.Request.RequestUri.AbsoluteUri.Contains("wikipedia.org"))
			{
				await e.Redirect("https://www.paypal.com");
			}
		}

		public async Task OnResponse(object sender, SessionEventArgs e)
		{
			//read response headers
			var responseHeaders = e.WebSession.Response.ResponseHeaders;

			// print out process id of current session
			// Console.WriteLine($"PID: {e.WebSession.ProcessId.Value}");

			Console.WriteLine(e.WebSession.Request.RequestUri.AbsoluteUri);

			//if (!e.ProxySession.Request.Host.Equals("medeczane.sgk.gov.tr")) return;
			if (e.WebSession.Request.Method == "GET" || e.WebSession.Request.Method == "POST")
			{
				if (e.WebSession.Response.ResponseStatusCode == "200")
				{
					if (e.WebSession.Response.ContentType != null && e.WebSession.Response.ContentType.Trim().ToLower().Contains("text/html"))
					{
						byte[] bodyBytes = await e.GetResponseBody();
						await e.SetResponseBody(bodyBytes);

						string body = await e.GetResponseBodyAsString();
						await e.SetResponseBodyString(body);
					}
				}
			}
		}
	}
}
