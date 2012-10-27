using System;
using System.Text;
using System.IO;
using System.Speech.Recognition;
using System.Speech.AudioFormat;
using System.Collections.Generic;
using Microsoft.Kinect;

namespace encausse.net {

  public class WSRKinectMacro : WSRMacro{
    
    // ==========================================
    //  WSRMacro CONSTRUCTOR
    // ==========================================

    public WSRKinectMacro(List<String> dir, double confidence, String server, String port, int loopback, List<string> context, bool gesture)
      : base(dir, confidence, server, port, loopback, context) {
      this.gesture = gesture;
    }

    // ==========================================
    //  KINECT GESTURE
    // ==========================================

    bool gesture = false;
    public void SetupSkeleton(KinectSensor sensor) {
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
    //  KINECT AUDIO
    // ==========================================

    protected KinectSensor sensor = null;
    public override Boolean SetupDevice(SpeechRecognitionEngine sre) {

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
        return false;
      }

      // Use Skeleton Engine
      SetupSkeleton(sensor);

      // Starting the sensor                   
      try { sensor.Start(); }
      catch (IOException) { sensor = null; return false; } // Some other application is streaming from the same Kinect sensor

      SetupAudiSource(sensor, sre);

      log("KINECT", "Using Kinect Sensors !"); 
      return true;
    }

    protected Boolean SetupAudiSource(KinectSensor sensor, SpeechRecognitionEngine sre) {
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

    public override void StopRecognizer() {
      log("KINECT", "StopRecognizer"); 
      base.StopRecognizer();
      if (sensor != null) {
        sensor.Stop();
      }
    }

    public override void StartRecognizer() {
      log("KINECT", "StartRecognizer"); 
      if (sensor != null) {
        sensor.Start();
        SetupAudiSource(sensor, GetEngine()); 
      }
      base.StartRecognizer();
    } 
  }
}