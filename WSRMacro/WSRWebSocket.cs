using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Microsoft.Kinect;
using Fleck;

namespace net.encausse.sarah {

  public class WebSocketManager  {

    // ==========================================
    //  WEBSOCKET MANAGER
    // ==========================================

    private WSRKinectMacro wsr;
    public WebSocketManager(WSRKinectMacro wsr) {
      this.wsr = wsr;
    }

    protected WebSocketServer websocket = null;
    List<IWebSocketConnection> sockets = new List<IWebSocketConnection>();

    public bool SetupWebSocket() {
      if (WSRConfig.GetInstance().websocket < 0) {
        return false;
      }
      websocket = new WebSocketServer("ws://localhost:" + WSRConfig.GetInstance().websocket);
      websocket.Start(socket => {
        socket.OnOpen = () => {
          WSRConfig.GetInstance().logInfo("WEBSCK", "Connected to: " + socket.ConnectionInfo.ClientIpAddress);
          sockets.Add(socket);
        };
        socket.OnClose = () => {
          WSRConfig.GetInstance().logInfo("WEBSCK", "Disconnected from: " + socket.ConnectionInfo.ClientIpAddress);
          sockets.Remove(socket);
        };
        socket.OnMessage = message => {
          SendWebSocket();
        };
      });
      return true;
    }

    // ==========================================
    //  COLOR FRAME
    // ==========================================

    public void SensorColorFrameReady(object sender, ColorImageFrameReadyEventArgs e) {
      // SendWebSocket();
    }

    private void SendWebSocket() {

      WriteableBitmap bitmap = wsr.NewColorBitmap(); // Always because of thread sockets
      Bitmap image = wsr.GetColorPNG(bitmap);
      MemoryStream ms = new MemoryStream();
      image.Save(ms, ImageFormat.Jpeg);
      image.Dispose();

      byte[] imgByte = ms.ToArray();
      string base64String = Convert.ToBase64String(imgByte);
      SendWebSocket(base64String);
    }

    protected void SendWebSocket(string message) {
      if (websocket == null) { return; }

      foreach (var socket in sockets) {
        socket.Send(message);
      }
    }
  }
}
