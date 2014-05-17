using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

using CUETools.Codecs;
using CUETools.Codecs.FLAKE;


namespace net.encausse.sarah {
  public class SpeechToText {
    private string endpointAddress;

    public SpeechToText()
      : this("https://www.google.com/speech-api/v2/recognize?xjerr=1&client=chromium", CultureInfo.CurrentCulture) {
    }

    public SpeechToText(string endpointAddress, CultureInfo culture) {
      this.endpointAddress = endpointAddress + "&lang=" + culture.Name;
    }

    public String Recognize(Stream contentToRecognize) {
      var request = (HttpWebRequest)WebRequest.Create(this.endpointAddress + "&maxresults=6&pfilter=2");
      ConfigureRequest(request);
      var requestStream = request.GetRequestStream();
      ConvertToFlac(contentToRecognize, requestStream);

      var response = request.GetResponse();
      var result = "";
      using (var responseStream = response.GetResponseStream()) {
        using (var zippedStream = new GZipStream(responseStream, CompressionMode.Decompress)) {
          using (var sr = new StreamReader(zippedStream)) {
            result = sr.ReadToEnd();
            WSRConfig.GetInstance().logInfo("[Google Recognize]", result);
          }

          // {"result":[{"alternative":[{"transcript":"qu'est-ce que tu fais","confidence":0.77608585},{"transcript":"qu'est-ce que tu fait"}],"final":true}],"result_index":0}
          JavaScriptSerializer serializer = new JavaScriptSerializer();
          var json = (IDictionary<string, object>) serializer.DeserializeObject(result);
          WSRConfig.GetInstance().logInfo("[Google Recognize]", json["result"].ToString());
        }
      }
      response.Close();
      return "";
    }

    private static void ConfigureRequest(HttpWebRequest request) {
      request.KeepAlive = true;
      request.SendChunked = true;
      request.ContentType = "audio/x-flac; rate=16000";
      request.UserAgent = "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/535.2 (KHTML, like Gecko) Chrome/15.0.874.121 Safari/535.2";
      request.Headers.Set(HttpRequestHeader.AcceptEncoding, "gzip,deflate,sdch");
      request.Headers.Set(HttpRequestHeader.AcceptLanguage, "en-GB,en-US;q=0.8,en;q=0.6");
      request.Headers.Set(HttpRequestHeader.AcceptCharset, "ISO-8859-1,utf-8;q=0.7,*;q=0.3");
      request.Method = "POST";
    }

    private void ConvertToFlac(Stream sourceStream, Stream destinationStream) {
      var audioSource = new WAVReader(null, sourceStream);
      try {
        if (audioSource.PCM.SampleRate != 16000) {
          throw new InvalidOperationException("Incorrect frequency - WAV file must be at 16 KHz.");
        }
        var buff = new AudioBuffer(audioSource, 0x10000);
        var flakeWriter = new FlakeWriter(null, destinationStream, audioSource.PCM);
        //flakeWriter.CompressionLevel = 8;
        while (audioSource.Read(buff, -1) != 0) {
          flakeWriter.Write(buff);
        }
        flakeWriter.Close();
      }
      finally {
        audioSource.Close();
      }
    }
  }
}
