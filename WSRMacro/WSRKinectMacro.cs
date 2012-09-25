using System;
using System.Text;
using System.IO;
using System.Speech.Recognition;
using System.Speech.AudioFormat;
using System.Collections.Generic;
using Microsoft.Kinect;

namespace encausse.net {

  class WSRKinectMacro : WSRMacro{

    // ==========================================
    //  WSRMacro CONSTRUCTOR
    // ==========================================

    public WSRKinectMacro(List<String> dir, double confidence, String server, String port)
      : base(dir, confidence, server, port) {
    }

    // ==========================================
    //  WSRMacro SENSOR
    // ==========================================

    // See SDK Speech Sample for more infos
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

      // Starting the sensor                   
      try { sensor.Start(); }
      catch (IOException) { sensor = null; return false; } // Some other application is streaming from the same Kinect sensor

      // Use Audio Source to Engine
      KinectAudioSource source = sensor.AudioSource;

      log(-1, "KINECT", "AutomaticGainControlEnabled : " + source.AutomaticGainControlEnabled);
      log(-1, "KINECT", "BeamAngle : " + source.BeamAngle);
      log(-1, "KINECT", "EchoCancellationMode : " + source.EchoCancellationMode);
      log(-1, "KINECT", "EchoCancellationSpeakerIndex : " + source.EchoCancellationSpeakerIndex);
      log(-1, "KINECT", "NoiseSuppression : " + source.NoiseSuppression);
      log(-1, "KINECT", "SoundSourceAngle : " + source.SoundSourceAngle);
      log(-1, "KINECT", "SoundSourceAngleConfidence : " + source.SoundSourceAngleConfidence);
      

      sre.SetInputToAudioStream(source.Start(), new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
      log("KINECT", "Using Kinect Sensors !"); 
      return true;
    }
  }
}