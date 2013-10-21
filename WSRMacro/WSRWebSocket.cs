using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Collections.Generic;

using Microsoft.Kinect;
using Fleck;

using Emgu.CV;
using Emgu.CV.Structure;

namespace net.encausse.sarah {

  public class WebSocketManager {

    // Singleton
    private static WebSocketManager manager;
    public static WebSocketManager GetInstance() {
      if (manager == null) {
        manager = new WebSocketManager();
      }
      return manager;
    }

    // ==========================================
    //  WEBSOCKET MANAGER
    // ==========================================

    WebSocketServer Server { get; set; }
    List<IWebSocketConnection> Sockets { get; set; }

    public bool StartWebSocketServer() {
      WSRConfig cfg = WSRConfig.GetInstance();

      int port = cfg.WebSocket;
      if (port < 0) { return false; }

      if (cfg.WSSmooth)  filter1 = new DepthFilteredSmoothing();
      if (cfg.WSAverage) filter2 = new DepthAveragedSmoothing();

      ImageFormat format = cfg.WSType == "png" ? ImageFormat.Png : ImageFormat.Jpeg;

      Sockets = new List<IWebSocketConnection>();
      Server = new WebSocketServer("ws://localhost:" + port);
      Server.Start(socket => {
        socket.OnOpen = () => {
          cfg.logInfo("WEBSCK", "Connected to: " + socket.ConnectionInfo.ClientIpAddress);
          lock (Sockets) { Sockets.Add(socket); }
        };
        socket.OnClose = () => {
          cfg.logInfo("WEBSCK", "Disconnected from: " + socket.ConnectionInfo.ClientIpAddress);
          lock (Sockets) { Sockets.Remove(socket); }
        };
        socket.OnMessage = message => {
          SendWebSocket(socket, GreenScreen(message), format);
        };
      });
      return true;
    }

    // ==========================================
    //  WEBSOCKET
    // ==========================================

    private void SendWebSocket(IWebSocketConnection socket, Bitmap image, ImageFormat format) {

      String base64String = null;
      using (var ms = new MemoryStream()) {
        image.Save(ms, format);
        base64String = Convert.ToBase64String(ms.ToArray());
      }
      SendWebSocket(socket, base64String);
    }

    protected void SendWebSocket(IWebSocketConnection socket, String message) {
      if (Server == null || message == null) { return; }
      socket.Send(message);
    }

    // ==========================================
    //  GREEN SCREEN
    // ==========================================

    private DepthImagePixel[] depthPixels;
    private ColorImagePoint[] colorCoordinates;
    private Image<Gray, Byte> mask;

    private int colorToDepthDivisor;
    private int depthW, depthH, colorW, colorH, depthL;

    private DepthFilteredSmoothing filter1;
    private DepthAveragedSmoothing filter2;

    public Bitmap GreenScreen(String message) {

      // 1. Retrieve Sensor
      WSRKinectSensor Sensor = ((WSRKinect)WSRConfig.GetInstance().WSR).ActiveSensor();
      Sensor.CopyDepthPixels = true;
      Sensor.CopyColorData   = true;

      // 2. Allocate data once
      if (null == mask) {
        depthW = Sensor.DepthW;
        depthH = Sensor.DepthH;
        colorW = Sensor.ColorW;
        colorH = Sensor.ColorH;
        depthL = Sensor.DepthPixels.Length;

        // Compute divisor
        colorToDepthDivisor = colorW / depthW;

        // Allocate space to put color pixel data
        colorCoordinates = new ColorImagePoint[depthL];

        // Allocate space to put the green pixels
        mask = new Image<Gray, Byte>(depthW, depthH);
      }

      // 3. Smooth DepthPixels
      this.depthPixels = Sensor.DepthPixels;
      if (null != filter1) {
        this.depthPixels = this.filter1.CreateFilteredDepthArray(this.depthPixels, depthW, depthH);
      }
      if (null != filter2) {
        this.depthPixels = this.filter2.CreateAverageDepthArray(this.depthPixels, depthW, depthH);
      }

      // 4. Map Depth to Sensor
      Sensor.Sensor.CoordinateMapper.MapDepthFrameToColorFrame(
                                     Sensor.DepthFormat, depthPixels,
                                     Sensor.ColorFormat, colorCoordinates);

      // 5. CleanUp 
      mask.SetZero();

      // 6. Loop over each row and column of the depth
      for (int y = 0; y < depthH; ++y) {
        for (int x = 0; x < depthW; ++x) {

          // calculate index into depth array
          int depthIndex = x + (y * depthW);

          // if we're tracking a player for the current pixel, do green screen
          DepthImagePixel depthPixel = depthPixels[depthIndex];
          int player = depthPixel.PlayerIndex;
          if (player <= 0) { continue; }

          // retrieve the depth to color mapping for the current depth pixel
          ColorImagePoint colorImagePoint = colorCoordinates[depthIndex];

          // scale color coordinates to depth resolution
          int colorInDepthX = colorImagePoint.X / colorToDepthDivisor;
          int colorInDepthY = colorImagePoint.Y / colorToDepthDivisor;

          // make sure the depth pixel maps to a valid point in color space
          // check y > 0 and y < depthHeight to make sure we don't write outside of the array
          // check x > 0 instead of >= 0 since to fill gaps we set opaque current pixel plus the one to the left
          // because of how the sensor works it is more correct to do it this way than to set to the right
          if (colorInDepthX > 0 && colorInDepthX < depthW && colorInDepthY >= 0 && colorInDepthY < depthH) {
            // calculate index into the green screen pixel array
            // int greenScreenIndex = colorInDepthX + (colorInDepthY * depthW);

            // set opaque
            mask.Data[colorInDepthY, colorInDepthX, 0] = 1;

            // compensate for depth/color not corresponding exactly by setting the pixel 
            // to the left to opaque as well
            mask.Data[colorInDepthY, colorInDepthX-1, 0] = 1;
          }
        }
      }

      Bitmap bitmap = WSRKinectSensor.ToBitmap(Sensor.ColorData, colorW, colorH, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
      Image<Bgra, Byte> color = new Image<Bgra, Byte>(bitmap);
      color = color.Resize(depthW, depthH, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);
      color = color.Copy(mask);

      return color.Bitmap;
    }
  }
}
