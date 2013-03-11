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
          lock (sockets) { sockets.Add(socket); }
        };
        socket.OnClose = () => {
          WSRConfig.GetInstance().logInfo("WEBSCK", "Disconnected from: " + socket.ConnectionInfo.ClientIpAddress);
          lock (sockets) { sockets.Remove(socket); }
        };
        socket.OnMessage = message => {
          SendWebSocket(socket);
        };
      });
      return true;
    }

    // ==========================================
    //  GREEN SCREEN
    // ==========================================

    private int colorToDepthDivisor;
    private int depthWidth;
    private int depthHeight;
    private int opaquePixelValue = -1;

    private DepthImagePixel[] depthPixels;
    private ColorImagePoint[] colorCoordinates;
    private int[] greenPixels;

    private WriteableBitmap colorBitmap;
    private WriteableBitmap opacityMask;
    private ImageFormat format = ImageFormat.Png;

    public void SetupGreenScreen(KinectSensor sensor) {
      
      ImageFormat format = WSRConfig.GetInstance().websocktype == "png" ?  ImageFormat.Png : ImageFormat.Jpeg;

      // Turn on the depth stream to receive depth frames
      sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30); 
      depthWidth  = sensor.DepthStream.FrameWidth;
      depthHeight = sensor.DepthStream.FrameHeight;

      // Turn on Color Stream
      int colorWidth  = sensor.ColorStream.FrameWidth;
      int colorHeight = sensor.ColorStream.FrameHeight;
      colorToDepthDivisor = colorWidth / depthWidth;

      // Turn on Skeleton to get player masks

      // Allocate space to put the depth pixels we'll receive
      depthPixels = new DepthImagePixel[sensor.DepthStream.FramePixelDataLength];

      // Allocate space to put the green pixels
      greenPixels = new int[sensor.DepthStream.FramePixelDataLength];

      // Allocate space to put color pixel data
      colorCoordinates = new ColorImagePoint[sensor.DepthStream.FramePixelDataLength];

      // Add an event handler to be called whenever there is new depth frame data
      sensor.AllFramesReady += this.SensorAllFramesReady;
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

    String base64String;
    public void SensorAllFramesReady(object sender, AllFramesReadyEventArgs e) {

      bool depthReceived = false;
      bool colorReceived = false;

      using (DepthImageFrame depthFrame = e.OpenDepthImageFrame()) {
        if (null != depthFrame) {
          // Copy the pixel data from the image to a temporary array
          depthFrame.CopyDepthImagePixelDataTo(depthPixels);
          depthReceived = true;
        }
      }

      using (ColorImageFrame colorFrame = e.OpenColorImageFrame()) {
        if (null != colorFrame) {
          // Done by WSRKinectMacro
          colorReceived = true;
        }
      }

      if (depthReceived) {
        HandleDepth();
      }

      if (colorReceived) {
        HandleColor();
      }

      WSRKinectMacro wsr = (WSRKinectMacro) WSRMacro.GetInstance();
      WriteableBitmap resize = colorBitmap.Resize(320, 240, WriteableBitmapExtensions.Interpolation.Bilinear);
      Bitmap image = wsr.GetColorPNG(resize, false);
      MemoryStream ms = new MemoryStream();
      
      image.Save(ms, format);
      image.Dispose();
      byte[] imgByte = ms.ToArray();
      base64String = Convert.ToBase64String(imgByte);

      // lock (sockets) {
      //   foreach (var socket in sockets) { SendWebSocket(socket, image); }
      //   image.Dispose();
      // }
    }

    private void HandleDepth() {
      KinectSensor sensor = ((WSRKinectMacro)WSRKinectMacro.GetInstance()).Sensor;
      sensor.CoordinateMapper.MapDepthFrameToColorFrame(
                    DepthImageFormat.Resolution640x480Fps30, depthPixels,
                    ColorImageFormat.RgbResolution640x480Fps30, colorCoordinates);

      Array.Clear(greenPixels, 0, greenPixels.Length);


      // loop over each row and column of the depth
      for (int y = 0; y < depthHeight; ++y) {
        for (int x = 0; x < depthWidth; ++x) {

          // calculate index into depth array
          int depthIndex = x + (y * depthWidth);

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
          if (colorInDepthX > 0 && colorInDepthX < depthWidth && colorInDepthY >= 0 && colorInDepthY < depthHeight) {
            // calculate index into the green screen pixel array
            int greenScreenIndex = colorInDepthX + (colorInDepthY * depthWidth);

            // set opaque
            greenPixels[greenScreenIndex] = opaquePixelValue;

            // compensate for depth/color not corresponding exactly by setting the pixel 
            // to the left to opaque as well
            greenPixels[greenScreenIndex - 1] = opaquePixelValue;
          }
        }
      }
    }

    private Int32Rect greenRect;
    private Rect maskRect;
    private System.Windows.Point maskPoint;
 
    private void HandleColor() {
      WSRKinectMacro wsr = (WSRKinectMacro)WSRKinectMacro.GetInstance();

      if (colorBitmap == null) {
        colorBitmap = wsr.NewColorBitmap();
        colorBitmap = BitmapFactory.ConvertToPbgra32Format(colorBitmap);

        opacityMask = new WriteableBitmap(depthWidth, depthHeight, 96, 96, PixelFormats.Bgra32, null);
        opacityMask = BitmapFactory.ConvertToPbgra32Format(opacityMask);

        maskPoint = new System.Windows.Point(0, 0);
        maskRect  = new Rect(0, 0, opacityMask.PixelWidth, opacityMask.PixelHeight);
        greenRect = new Int32Rect(0, 0, opacityMask.PixelWidth, opacityMask.PixelHeight);
      }

      // Write the color pixel data into our bitmap
      wsr.UpdateColorBitmap(colorBitmap);

      // Write the green pixel data into our bitmap
      opacityMask.WritePixels(greenRect, greenPixels, greenRect.Width * ((opacityMask.Format.BitsPerPixel + 7) / 8), 0);

      // Merge color and green data into our bitmap
      colorBitmap.Blit(maskPoint, opacityMask, maskRect, Colors.Black, WriteableBitmapExtensions.BlendMode.Mask);

    }

    // ==========================================
    //  WEBSOCKET
    // ==========================================

    private void SendWebSocket(IWebSocketConnection socket) {
      // WSRKinectMacro wsr = (WSRKinectMacro)WSRMacro.GetInstance();
      // WriteableBitmap bitmap = wsr.NewColorBitmap(); // Always because of thread sockets
      // Bitmap image = wsr.GetColorPNG(colorBitmap, true);
      SendWebSocket(socket, base64String);
      // image.Dispose();
    }

    private void SendWebSocket(IWebSocketConnection socket, Bitmap image) {
      MemoryStream ms = new MemoryStream();
      image.Save(ms, ImageFormat.Png);

      byte[] imgByte = ms.ToArray();
      string base64String = Convert.ToBase64String(imgByte);
      SendWebSocket(socket, base64String);
    }

    protected void SendWebSocket(IWebSocketConnection socket, String message) {
      if (websocket == null) { return; }
      if (message == null)   { return; }
      socket.Send(message);
    }
  }
}
