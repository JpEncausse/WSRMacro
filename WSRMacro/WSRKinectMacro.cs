using System;
using System.Globalization;
using System.Text;
using System.IO;
using System.Speech.Recognition;
using System.Speech.AudioFormat;
using System.Collections;
using System.Collections.Generic;
using System.Xml.XPath;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Kinect;
using NHttp;
using ZXing;
using Fleck;
using System.Drawing.Drawing2D;

namespace encausse.net {

  public class WSRKinectMacro : WSRMacro{
    
    // ==========================================
    //  WSRMacro CONSTRUCTOR
    // ==========================================

    public WSRKinectMacro(List<String> dir, double confidence, String server, String port, int loopback, List<string> context, bool gesture, bool picture, int websocket)
      : base(dir, confidence, server, port, loopback, context) {
      this.gesture = gesture;
      this.picture = picture;
      this.wckport = websocket;

      SetupSensor();
      SetupWebSocket();
    }

    // ==========================================
    //  KINECT GESTURE
    // ==========================================

    bool gesture = false;
    public void SetupSkeletonFrame(KinectSensor sensor) {
      if (!gesture) {
        return;
      }
      // Build Gesture Manager
      GestureManager mgr = new GestureManager(this);

      // Load Gestures from directories
      foreach (string directory in this.directories) {
        DirectoryInfo d = new DirectoryInfo(directory);
        mgr.LoadGestures(d);
      }

      // Plugin in Kinect Sensor
      log("KINECT", "Starting Skeleton sensor");
      sensor.SkeletonStream.Enable();
      sensor.SkeletonFrameReady += mgr.SensorSkeletonFrameReady;

    }

    public void HandleGestureComplete(Gesture gesture) {
      SendRequest(CleanURL(gesture.Url));
    }

    // ==========================================
    //  WSRMacro RECOGNIZE
    // ==========================================

    protected override String HandleCustomAttributes(XPathNavigator xnav) {
      String path = base.HandleCustomAttributes(xnav);

      // 3.3 Parse Result's Photo
      path = HandlePicture(xnav, path);

      return path;
    }

    protected String HandlePicture(XPathNavigator xnav, String path) {
      XPathNavigator picture = xnav.SelectSingleNode("/SML/action/@picture");
      if (picture != null) {
        path = TakePicture("medias/");
      }

      return path;
    }

    // ==========================================
    //  WSRMacro REQUEST
    // ==========================================

    protected override bool HandleCustomRequest(HttpRequestEventArgs e) {

      if (base.HandleCustomRequest(e)) {
        return true;
      }

      // 3.3 Parse Result's Photo
      if (e.Request.Params.Get("picture") != null) {
        String path = TakePicture("medias/");
        using (var writer = new StreamWriter(e.Response.OutputStream)) {
          e.Response.ContentType = "image/jpeg";
          Bitmap bmp = (Bitmap)Bitmap.FromFile(path);
          bmp.Save(e.Response.OutputStream, ImageFormat.Jpeg);
        }
        return true;
      }

      return false;
    } 

    // ==========================================
    //  KINECT COLOR FRAME
    // ==========================================
    protected bool picture = false;
    protected byte[] colorPixels;
    protected int colorW = 1280;
    protected int colorH = 960;

    public void SetupColorFrame(KinectSensor sensor) {
      log("KINECT", "Starting Color sensor");
      if (!picture) {
        return;
      }

      // Turn on the color stream to receive color frames
      sensor.ColorStream.Enable(ColorImageFormat.RgbResolution1280x960Fps12);
      // sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);

      colorW = sensor.ColorStream.FrameWidth;
      colorH = sensor.ColorStream.FrameHeight;

      // Allocate space to put the pixels we'll receive
      colorPixels = new byte[sensor.ColorStream.FramePixelDataLength];

      // Add an event handler to be called whenever there is new color frame data
      sensor.ColorFrameReady += new EventHandler<ColorImageFrameReadyEventArgs>(handle_ColorFrameReady);
    }

    protected void handle_ColorFrameReady(object sender, ColorImageFrameReadyEventArgs e) {

      using (ColorImageFrame colorFrame = e.OpenColorImageFrame()) {
        if (colorFrame == null) {
          return;
        }

        // Copy the pixel data from the image to a temporary array
        colorFrame.CopyPixelDataTo(colorPixels);
      }

      DecodeQRCode();
    }

    private Bitmap getBitmap() {
      WriteableBitmap colorBitmap = new WriteableBitmap(colorW, colorH, 96.0, 96.0, PixelFormats.Bgr32, null);
      colorBitmap.WritePixels(
            new Int32Rect(0, 0, colorBitmap.PixelWidth, colorBitmap.PixelHeight),
            colorPixels, colorBitmap.PixelWidth * sizeof(int), 0);

      // Create a png bitmap encoder which knows how to save a .png file
      BitmapEncoder encoder = new PngBitmapEncoder();

      // Create frame from the writable bitmap and add to encoder
      encoder.Frames.Add(BitmapFrame.Create(colorBitmap));

      Bitmap image = null;
      using (MemoryStream ms = new MemoryStream()) {
        encoder.Save(ms);
        image = (Bitmap)Bitmap.FromStream(ms);
      }

      image.RotateFlip(RotateFlipType.RotateNoneFlipX);
      return image;
    }

    // ==========================================
    //  KINECT WEBSOCKET
    // ==========================================

    protected int wckport = -1;
    protected WebSocketServer websocket = null;
    List<IWebSocketConnection> sockets = new List<IWebSocketConnection>();
    
    protected void SetupWebSocket() {
      if (!picture || wckport < 0) {
        return;
      }
      websocket = new WebSocketServer("ws://localhost:" + wckport);
      websocket.Start(socket => {
        socket.OnOpen = () => {
          log("WEBSCK", "Connected to: " + socket.ConnectionInfo.ClientIpAddress);
          sockets.Add(socket);
        };
        socket.OnClose = () => {
          log("WEBSCK", "Disconnected from: " + socket.ConnectionInfo.ClientIpAddress);
          sockets.Remove(socket);
        };
        socket.OnMessage = message => {
          // log("WEBSCK", "Message: " + message);
          SendWebSocket(colorPixels);
        };
      });
    }

    protected void SendWebSocket(byte[] message) {
      if (websocket == null) {
        return;
      }

      Bitmap image = getBitmap();
      /*
      Bitmap result = new Bitmap( 640, 480 );
      using (Graphics g = Graphics.FromImage((Image)result)) {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, 0, 0, 640, 480);
      }
      image.Dispose();
      */

      MemoryStream ms = new MemoryStream();
      image.Save(ms, ImageFormat.Jpeg);
      image.Dispose();

      byte[] imgByte = ms.ToArray();
      string base64String = Convert.ToBase64String(imgByte);
      SendWebSocket(base64String);
    }

    protected void SendWebSocket(string message) {
      if (websocket == null) {
        return;
      }

      foreach (var socket in sockets) {
        socket.Send(message);
      }
    }

    // ==========================================
    //  KINECT PICTURE
    // ==========================================


    private String TakePicture(string folder) {

      if (sensor == null || folder == null) {
        return null;
      }

      Bitmap image = getBitmap();
      BitmapEncoder encoder = new JpegBitmapEncoder();
      String time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);
      String path = folder+"KinectSnapshot-" + time + ".jpg";
      using (FileStream fs = new FileStream(path, FileMode.Create)) {
        image.Save(fs, ImageFormat.Jpeg);
      //image.Save(fs, encoder);
      }
      image.Dispose();

      log("PICTURE", "New picture to: " + path);
      return path;
    }

    // ==========================================
    //  KINECT QRCODE
    // ==========================================

    int threashold = 0;
    private bool DecodeQRCode() {

      if (sensor == null && threashold++ < 48) {
        return false;
      }
      threashold = 0;

      Bitmap image = getBitmap();
      BarcodeReader reader = new BarcodeReader { AutoRotate = true, TryHarder = true , PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE } };
      Result result = reader.Decode(image);
      image.Dispose();

      if (result == null) {
        return false;
      }

      // Play sound effect for feedback
      PlayMP3("medias/qrcode.mp3");

      // Do something with the result
      String type = result.BarcodeFormat.ToString();
      String content = result.Text;

      log("QRCODE", "Type: " + type + " Content: " + content);
      if (content.StartsWith("http")) {
        SendRequest(content);
      }
      else {
        Speak(content);
      }
      return true;
    }

    // ==========================================
    //  KINECT AUDIO
    // ==========================================

    public override Boolean SetupDevice(SpeechRecognitionEngine sre) {

      // Abort if there is no sensor available
      if (null == sensor) {
        log("KINECT", "No Kinect Sensor");
        return false;
      }

      SetupAudioSource(sensor, sre);

      log("KINECT", "Using Kinect Sensors !"); 
      return true;
    }


    protected Boolean SetupAudioSource(KinectSensor sensor, SpeechRecognitionEngine sre) {
      if (!sensor.IsRunning) {
        log("KINECT", "Sensor is not running"); 
        return false;
      }
      
      // Use Audio Source to Engine
      KinectAudioSource source = sensor.AudioSource;

      log(0, "KINECT", "AutomaticGainControlEnabled : " + source.AutomaticGainControlEnabled);
      log(0, "KINECT", "BeamAngle : " + source.BeamAngle);
      log(0, "KINECT", "EchoCancellationMode : " + source.EchoCancellationMode);
      log(0, "KINECT", "EchoCancellationSpeakerIndex : " + source.EchoCancellationSpeakerIndex);
      log(0, "KINECT", "NoiseSuppression : " + source.NoiseSuppression);
      log(0, "KINECT", "SoundSourceAngle : " + source.SoundSourceAngle);
      log(0, "KINECT", "SoundSourceAngleConfidence : " + source.SoundSourceAngleConfidence);

      sre.SetInputToAudioStream(source.Start(), new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
      return true;
    }

    protected override String GetDeviceConfidence() {
      if (sensor == null) { return ""; }
      KinectAudioSource source = sensor.AudioSource;
      if (source == null) { return ""; }
      return "BeamAngle : " + source.BeamAngle + " "
           + "SourceAngle : " + source.SoundSourceAngle + " "
           + "SourceConfidence : " + source.SoundSourceAngleConfidence;
    }

    // ==========================================
    //  KINECT STATRT/STOP
    // ==========================================

    protected KinectSensor sensor = null;
    protected void SetupSensor() {
      // Looking for a valid sensor 
      foreach (var potentialSensor in KinectSensor.KinectSensors) {
        if (potentialSensor.Status == KinectStatus.Connected) {
          sensor = potentialSensor;
          break;
        }
      }

      // Abort if there is no sensor available
      if (null == sensor) {
        log("KINECT", "No Kinect Sensor");
        return;
      }

      // Use Skeleton Engine
      SetupSkeletonFrame(sensor);

      // Use Color Engine
      SetupColorFrame(sensor);

      // Starting the sensor                   
      try { sensor.Start(); }
      catch (IOException) { sensor = null; return; } // Some other application is streaming from the same Kinect sensor
    }
    /*
    public override void StopRecognizer() { 
      base.StopRecognizer();
      log("KINECT", "Stop sensor"); 
      if (sensor != null) {
        sensor.Stop();
      }
      log("KINECT", "Stop sensor...done");
    }

    public override void StartRecognizer() {
      log("KINECT", "Start sensor"); 
      if (sensor != null) {
        sensor.Start();
        SetupAudioSource(sensor, GetEngine()); 
      }
      log("KINECT", "Start sensor...done");
      base.StartRecognizer();
    }
    */
  }
}