using System;
using System.IO;
using System.Net;
using System.Text;

using NHttp;
using System.Web;

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

    private bool working = false;
    protected String CleanURL(string url) {
      return url.Replace("http://127.0.0.1:8080", WSRConfig.GetInstance().GetRemoteURL());
    }

    protected String AppendURL(string url, string param, string value) {
      value = HttpUtility.UrlEncode(value);
      if (url.IndexOf('?') < 0)
        return url + "?" + param + "=" + value;
      return url + (url.EndsWith("&") ? "" : "&") + param + "=" + value;
    }

    public void SendRequest(string url) {
      if (url == null) { return; }
      if (working) { return; }
      
      working = true;

      // Clean URL
      url = CleanURL(url);

      // Append ClientId
      url = AppendURL(url, "client", WSRConfig.GetInstance().Id);

      // Append UserId
      var profile = WSRProfileManager.GetInstance().Current;
      if (null != profile){
        url = AppendURL(url, "profile", profile.Name);
      }

      // Append Directory Path
      url = AppendURL(url, "directory", WSRSpeechManager.GetInstance().GetGrammarPath());

      WSRConfig.GetInstance().logInfo("HTTP", "Build HttpRequest: " + url);
      HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
      req.Method = "GET";

      WSRConfig.GetInstance().logInfo("HTTP", "Send HttpRequest: " + req.Address);
      try {
        HttpWebResponse res = (HttpWebResponse)req.GetResponse();
        using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) {
          WSRSpeakerManager.GetInstance().Speak(sr.ReadToEnd(), false);
        }
      }
      catch (WebException ex) {
        WSRConfig.GetInstance().logError("HTTP", ex);
      }
      working = false;
    }

    public void SendPost(string url, string key, string value) {
      SendPost(url, new String[] { key }, new String[] { value });
    }

    public void SendPost(string url, string[] keys, string[] values) {
      if (url == null)  { return; }
      if (keys == null || values == null) { return; }
      if (working) { return; } working = true;

      // Clean URL
      url = CleanURL(url);

      // POST Data
      StringBuilder postData = new StringBuilder();
      for (int i=0; i < keys.Length; i++) {
        postData.Append(keys[i] + "=" + HttpUtility.UrlEncode(values[i]) + "&");
      }
      ASCIIEncoding ascii = new ASCIIEncoding();
      byte[] postBytes = ascii.GetBytes(postData.ToString());

      // Build request
      WSRConfig.GetInstance().logInfo("HTTP", "Build POSTRequest: " + url);
      HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
      req.Method = "POST";
      req.ContentType = "application/x-www-form-urlencoded";
      req.ContentLength = postBytes.Length;

      // Send POST data
      WSRConfig.GetInstance().logInfo("HTTP", "Send POSTRequest: " + req.Address);
      try {
        Stream postStream = req.GetRequestStream();
        postStream.Write(postBytes, 0, postBytes.Length);
        postStream.Flush();
        postStream.Close();
      }
      catch (WebException ex) {
        WSRConfig.GetInstance().logError("HTTP", ex);
      }
      working = false;
    }

    public void SendUpoad(string url, string path) {
      if (url == null) { return; }
      if (path == null) { SendRequest(url); return; }

      if (working) { return; } working = true;

      url = CleanURL(url);
      WSRConfig.GetInstance().logInfo("HTTP", "Build UploadRequest: " + url);

      WebClient client = new WebClient();
      client.Headers.Add("user-agent", "S.A.R.A.H. (Self Actuated Residential Automated Habitat)");

      try {
        byte[] responseArray = client.UploadFile(url, path);
        String response = System.Text.Encoding.ASCII.GetString(responseArray);
        WSRSpeakerManager.GetInstance().Speak(response, false);
      }
      catch (Exception ex) {
        WSRConfig.GetInstance().logInfo("HTTP", "Exception: " + ex.Message);
      }
      working = false;
    }

    // ==========================================
    //  WSRMacro HTTP SERVER
    // ==========================================

    HttpServer http = null;
    HttpServer httpLocal = null;
    public void StartHttpServer() {

      int port = WSRConfig.GetInstance().loopback;
      IPAddress address = GetIpAddress();

      // 192.168.0.x
      if (address != null){
        try {
          http = new HttpServer();
          http.EndPoint = new IPEndPoint(address, port);
          http.Start();
          http.RequestReceived += this.http_RequestReceived;
          WSRConfig.GetInstance().logInfo("INIT", "Starting Server: http://" + http.EndPoint + "/");
        }
        catch (Exception ex) {
          http = null;
          WSRConfig.GetInstance().logInfo("HTTP", "Exception: " + ex.Message);
        }
      }

      // Localhost
      try {
        httpLocal = new HttpServer();
        httpLocal.EndPoint = new IPEndPoint(IPAddress.Loopback, port);
        httpLocal.RequestReceived += this.http_RequestReceived;
        httpLocal.Start();
        WSRConfig.GetInstance().logInfo("INIT", "Starting Server: http://" + httpLocal.EndPoint + "/");
      }
      catch (Exception ex) {
        httpLocal = null;
        WSRConfig.GetInstance().logInfo("HTTP", "Exception: " + ex.Message);
      }
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
      return null;
    }

    protected void http_RequestReceived(object sender, HttpRequestEventArgs e) {
      WSRConfig.GetInstance().logInfo("HTTP", "Request received: " + e.Request.Url.AbsoluteUri);

      // Fake response
      using (var writer = new StreamWriter(e.Response.OutputStream)) {

        // Handle custom request
        WSRConfig.GetInstance().WSR.HandleCustomRequest(e, writer);
      
        writer.Write(" ");
        writer.Flush();
        writer.Close();
      }
    }
  }
}