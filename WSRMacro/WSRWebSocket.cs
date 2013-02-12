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

    protected WebSocketServer websocket = null;
    List<IWebSocketConnection> sockets = new List<IWebSocketConnection>();

    public bool SetupWebSocket() {
      if (WSRConfig.GetInstance().websocket <= 0) {
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
          SendWebSocket(socket);
        };
      });
      return true;
    }

    // ==========================================
    //  COLOR FRAME
    // ==========================================

    // Do not send on FrameReady, instead wait sockets to ask for
    public void SensorColorFrameReady(object sender, ColorImageFrameReadyEventArgs e) {
      // foreach (var socket in sockets) {
      //   SendWebSocket(socket);
      // }
    }


    private void SendWebSocket(IWebSocketConnection socket) {
      WSRKinectMacro wsr = (WSRKinectMacro) WSRMacro.GetInstance();
      WriteableBitmap bitmap = wsr.NewColorBitmap(); // Always because of thread sockets
      Bitmap image = wsr.GetColorPNG(bitmap);
      MemoryStream ms = new MemoryStream();
      image.Save(ms, ImageFormat.Jpeg);
      image.Dispose();

      byte[] imgByte = ms.ToArray();
      string base64String = Convert.ToBase64String(imgByte);
      SendWebSocket(socket, base64String);
    }

    protected void SendWebSocket(IWebSocketConnection socket, String message) {
      if (websocket == null) { return; }
      socket.Send(message);
    }
  }
}
