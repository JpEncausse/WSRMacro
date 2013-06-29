using System;
using System.IO;
using System.Net;
using System.Text;

using NHttp;

namespace net.encausse.sarah {

  public class WSRHttpManager {

    // Singleton
    private static WSRHttpManager manager;
    public static WSRHttpManager GetInstance() {
      if (manager == null) {
        manager = new WSRHttpManager();
      }
      return manager;
    }

    // ==========================================
    //  WSRMacro HTTP REQUEST
    // ==========================================

    protected String CleanURL(String url) {
      return url.Replace("http://127.0.0.1:8080", WSRConfig.GetInstance().GetRemoteURL());
    }

    public void SendRequest(String url) {
      if (url == null) { return; }
      url = CleanURL(url);
      WSRConfig.GetInstance().logInfo("HTTP", "Build HttpRequest: " + url);

      HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
      req.Method = "GET";

      WSRConfig.GetInstance().logInfo("HTTP", "Send HttpRequest: " + req.Address);

      try {
        HttpWebResponse res = (HttpWebResponse)req.GetResponse();
        using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) {
          WSRSpeaker.GetInstance().Speak(sr.ReadToEnd(), true);
        }
      }
      catch (WebException ex) {
        WSRConfig.GetInstance().logError("HTTP", ex);
      }
    }

    public void SendRequest(String url, String path) {
      if (url == null) { return; }
      if (path == null) { SendRequest(url); return; }
      url = CleanURL(url);

      WSRConfig.GetInstance().logInfo("HTTP", "Build HttpRequest: " + url);

      WebClient client = new WebClient();
      client.Headers.Add("user-agent", "S.A.R.A.H. (Self Actuated Residential Automated Habitat)");

      try {
        byte[] responseArray = client.UploadFile(url, path);
        String response = System.Text.Encoding.ASCII.GetString(responseArray);
        WSRSpeaker.GetInstance().Speak(response, true);
      }
      catch (Exception ex) {
        WSRConfig.GetInstance().logInfo("HTTP", "Exception: " + ex.Message);
      }
    }

    // ==========================================
    //  WSRMacro HTTP SERVER
    // ==========================================

    HttpServer http = null;
    HttpServer httpLocal = null;
    public void StartHttpServer() {

      int port = WSRConfig.GetInstance().loopback;

      // 192.168.0.x
      try {
        http = new HttpServer();
        http.EndPoint = new IPEndPoint(GetIpAddress(), port);
        http.Start();
        http.RequestReceived += this.http_RequestReceived;
        WSRConfig.GetInstance().logInfo("INIT", "Starting Server: http://" + http.EndPoint + "/");
      }
      catch (Exception ex) {
        http = null;
        WSRConfig.GetInstance().logInfo("HTTP", "Exception: " + ex.Message);
      }

      // Localhost
      httpLocal = new HttpServer();
      httpLocal.EndPoint = new IPEndPoint(IPAddress.Loopback, port);
      httpLocal.RequestReceived += this.http_RequestReceived;
      httpLocal.Start();
      WSRConfig.GetInstance().logInfo("INIT", "Starting Server: http://" + httpLocal.EndPoint + "/");
    }

    public void Dispose() {
      if (http != null) {
        http.Stop();
        http.Dispose();
      }

      if (httpLocal != null) {
        httpLocal.Stop();
        httpLocal.Dispose();
      }
    }

    protected IPAddress GetIpAddress() {
      IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
      foreach (IPAddress ip in host.AddressList) {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
          return ip;
        }
      }
      return IPAddress.Loopback;
    }

    protected void http_RequestReceived(object sender, HttpRequestEventArgs e) {
      WSRConfig.GetInstance().logInfo("HTTP", "Request received: " + e.Request.Url.AbsoluteUri);

      // Handle custom request
      WSRConfig.GetInstance().GetWSRMicro().HandleCustomRequest(e);
      
      // Fake response
      using (var writer = new StreamWriter(e.Response.OutputStream)) {
        writer.Write(" ");
        writer.Flush();
        writer.Close();
      }
    }
  }
}
