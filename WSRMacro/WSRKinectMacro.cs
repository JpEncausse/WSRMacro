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

    public WSRKinectMacro(List<String> dir, double confidence, String server, String port)
      : base(dir, confidence, server, port) {
    }

    // ==========================================
    //  KINECT GESTURE
    // ==========================================


    public void SetupSkeleton(KinectSensor sensor) {
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
      
      log("KINECT", "Using Kinect Sensors !"); 
      return true;
    }
  }
}