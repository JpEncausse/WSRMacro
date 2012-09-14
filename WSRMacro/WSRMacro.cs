using System;
using System.Speech;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using System.IO;
using System.Xml.XPath;
using System.Net;
using System.Threading;
using System.Text;
using System.Web;
using System.Globalization;

namespace encausse.net
{
    /**
     * AUTOMATE
     * ********
     *                         * * * * * * * * * * * * *
     *                         *                       *
     * Main => SetGrammar => Start => Recognized => Complete
     *             *           *           *
     *             *           *           *
     *          Watcher   LoadGrammar    HTTP
     */

    class WSRMacro {

        // -----------------------------------------
        //  MAIN
        // -----------------------------------------
        /*
        static void Main(string[] args){

            String directory = "macros";
            if (args.Length >= 1){
                directory = args[0];
            }

            String server = "192.168.0.8";
            if (args.Length >= 2) {
                server = args[1];
            }
            
            double confidence = 0.80;
            if (args.Length >= 3) {
              CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");
              confidence = double.Parse(args[2], culture);
            }
         
            try {
                WSRMacro wsr = new WSRMacro(directory, server, confidence);
            } 
            catch (Exception ex){
                Console.WriteLine(ex);
            }

            // Keep the console window open.
            Console.ReadLine();
        }
        */
        // -----------------------------------------
        //  WSRMacro VARIABLES
        // -----------------------------------------

        private double CONFIDENCE = 0.82;
        private double CONFIDENCE_DICTATION = 0.4;

        private SpeechRecognitionEngine recognizer = null;
        DictationGrammar dication = null;

        private String server = "192.168.0.8";
        private String directory = null;
        private String abspath   = null; // Resolved absolute path
        private FileSystemWatcher watcher = null;
        
        private int loading = 0;       // Files to load
        private Boolean load = false;  // Flags to trigger load;
        private Boolean start = false; // Recognizer status

        private String dictationUrl = null; // Last dication URL

        // -----------------------------------------
        //  WSRMacro CONSTRUCTOR
        // -----------------------------------------

        public WSRMacro() { }

        public WSRMacro(String directory, String server, double confidence) {
            
            this.server = server;
            this.CONFIDENCE = confidence;

            Console.WriteLine("Server IP: " + server);
            Console.WriteLine("Confidence: " + confidence);

            SetGrammar(directory);
        }

        // -----------------------------------------
        //  WSRMacro METHODS
        // -----------------------------------------

        public Grammar GetGrammar(String file) {

            // Grammar grammar = new Grammar(new FileStream(file, FileMode.Open), null, baseURI);
            Grammar grammar = new Grammar(file);
            grammar.Enabled = true;

            return grammar;
        }

        public void LoadGrammar(String file, String name){

            // Get the Grammar
            Grammar grammar = GetGrammar(file);
            grammar.Name = name;

            // Get recognizer
            SpeechRecognitionEngine sre = GetEngine();

            // Load the grammar object to the recognizer.
            Console.WriteLine("[Grammar] Load file: " + name + " : " + file);
            sre.LoadGrammarAsync(grammar);
        }

        protected void LoadGrammar() {
            this.loading = 0;

            // Unload All Grammar
            Console.WriteLine("[Grammar] Unload");
            SpeechRecognitionEngine sre = GetEngine();
            sre.UnloadAllGrammars();

            // Load Grammar
            DirectoryInfo dir = new DirectoryInfo(directory);
            abspath = dir.FullName;

            Console.WriteLine("[Grammar] Load directory: " + abspath);
            foreach (FileInfo f in dir.GetFiles("*.xml")) {
                this.loading++;
                LoadGrammar(f.FullName, f.Name);
            }

            // Add a Dictation Grammar
            dication = new DictationGrammar("grammar:dictation");
            dication.Name = "dictation";
            dication.Enabled = false;
            GetEngine().LoadGrammarAsync(dication);
        }

        public void SetGrammar(String directory) {

            if (!Directory.Exists(directory)) {
                throw new Exception("Macro's directory do not exists: " + directory);
            }

            // Stop Directory Watcher
            StopDirectoryWatcher();

            this.directory = directory;
            this.load = true;

            // Set path to watcher
            GetDirectoryWatcher().Path = directory;

            // Start Automate
            StartRecognizer();
        }

        // ------------------------------------------
        //  WSRMacro WATCHER
        // ------------------------------------------

        public FileSystemWatcher GetDirectoryWatcher() {
            if (watcher != null) {
                return watcher;
            }

            Console.WriteLine("[Watcher] Init watcher");
            watcher = new FileSystemWatcher();
            watcher.Path = directory;
            watcher.Filter = "*.xml";
            watcher.IncludeSubdirectories = false;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Changed += new FileSystemEventHandler(watcher_Changed);

            return watcher;
        }

        public void StartDirectoryWatcher() {
            if (!GetDirectoryWatcher().EnableRaisingEvents) {
                Console.WriteLine("[Watcher] Start watching");
                GetDirectoryWatcher().EnableRaisingEvents = true;
            }
        }

        public void StopDirectoryWatcher() {
            if (GetDirectoryWatcher().EnableRaisingEvents) {
                Console.WriteLine("[Watcher] Stop watching");
                GetDirectoryWatcher().EnableRaisingEvents = false;
            }
        }

        // ------------------------------------------
        //  WSRMacro ENGINE
        // ------------------------------------------

        public SpeechRecognitionEngine GetEngine() {
            if (recognizer != null) {
                return recognizer;
            }
            
            // For Kinect use this:
            // http://social.msdn.microsoft.com/Forums/en-US/kinectsdkaudioapi/thread/f184a652-a63f-4c72-a807-f9770fdf57f8

            Console.WriteLine("[Engine] Init recognizer");
            recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("fr-FR"));

            // Add a handler for the LoadGrammarCompleted event.
            recognizer.LoadGrammarCompleted += new EventHandler<LoadGrammarCompletedEventArgs>(recognizer_LoadGrammarCompleted);

            // Add a handler for the SpeechRecognized event.
            recognizer.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);

            // Add a handler for the SpeechRecognizedCompleted event.
            recognizer.RecognizeCompleted += new EventHandler<RecognizeCompletedEventArgs>(recognizer_RecognizeCompleted);

            // Add a handler for the AudioStateChangedEvent event.
            recognizer.AudioStateChanged += new EventHandler<AudioStateChangedEventArgs>(recognizer_AudioStateChanged);

            // Set recognizer properties
            recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
            recognizer.BabbleTimeout = TimeSpan.FromSeconds(2);
            recognizer.EndSilenceTimeout = TimeSpan.FromSeconds(1);
            recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

            Console.WriteLine("BabbleTimeout: {0}", recognizer.BabbleTimeout);
            Console.WriteLine("InitialSilenceTimeout: {0}", recognizer.InitialSilenceTimeout);
            Console.WriteLine("EndSilenceTimeout: {0}", recognizer.EndSilenceTimeout);
            Console.WriteLine("EndSilenceTimeoutAmbiguous: {0}", recognizer.EndSilenceTimeoutAmbiguous);

            // Set Max Alternate to 0
            //recognizer.MaxAlternates = 0;
            Console.WriteLine("MaxAlternates: {0}", recognizer.MaxAlternates);

            // Set the input to the recognizer.
            recognizer.SetInputToDefaultAudioDevice();

            return recognizer;
        }

        // See also: http://msdn.microsoft.com/en-us/library/ms554584.aspx
        public void StartRecognizer() {

            // Request a loading of grammar
            if (this.load){
              this.load = false;
              LoadGrammar();
              return;
            }

            // Prevent lasting call during grammar
            if (this.loading > 0) { return; }

            // Start Directory Watcher
            StartDirectoryWatcher();

            // Start Recognizer
            if (!this.start) {
              this.start = true;
              GetEngine().RecognizeAsync(RecognizeMode.Single);
              // Console.WriteLine("[Engine] Start listening " + GetEngine().AudioLevel);
            }
        }

        public void StopRecognizer() {
            Console.WriteLine("[Engine] Stop listening");
            GetEngine().RecognizeAsyncStop();
        }

        // ------------------------------------------
        //  WSRMacro HTTP
        // ------------------------------------------

        protected Boolean hasDictation(XPathNavigator xnav) {
            XPathNavigator dictation = xnav.SelectSingleNode("/SML/action/@dictation");
            if (dictation == null) { return false; }

            dication.Enabled = true;
            return true;
        }

        protected String GetResultTTS(XPathNavigator xnav) {
            XPathNavigator tts = xnav.SelectSingleNode("/SML/action/@tts");
            if (tts != null) { return tts.Value; }
            return null;
        }

        protected String BuildResultURL(XPathNodeIterator it) {
            String qs = "";
            while (it.MoveNext()) {
                String children = "";
                if (it.Current.Name == "confidence") continue;
                if (it.Current.Name == "uri") continue;
                if (it.Current.HasChildren) {
                  children = BuildResultURL(it.Current.SelectChildren(String.Empty, it.Current.NamespaceURI));
                }
                qs += (children == "") ? (it.Current.Name + "=" + it.Current.Value + "&") : (children);
            }
            return qs;
        }
        
        protected String GetResultURL(XPathNavigator xnav) {
            XPathNavigator xurl = xnav.SelectSingleNode("/SML/action/@uri");
            if (xurl == null) { return null; }

            // Build URI
            String url = xurl.Value + "?";
            url = url.Replace("http://127.0.0.1:", "http://"+server+":");

            // Build QueryString
            url += BuildResultURL(xnav.Select("/SML/action/*"));

            // Append Directory Path
            url += "directory=" + abspath;

            return url;
        }

        protected double GetResultThreashold(XPathNavigator xnav) {
          XPathNavigator level = xnav.SelectSingleNode("/SML/action/@threashold");
          if (level != null) {
            Console.WriteLine("Setting confidence level: " + level.Value);
            return level.ValueAsDouble; 
          }
          return CONFIDENCE;
        }

        protected void SendRequest(String url) {
            if (url == null) { return; }

            Console.WriteLine("[HTTP] Build HttpRequest: " + url);

            HttpWebRequest req = (HttpWebRequest) WebRequest.Create(url);
            req.Method = "GET";

            Console.WriteLine("[HTTP] Send HttpRequest: " + req.Address);
            
            try {
                HttpWebResponse res = (HttpWebResponse)req.GetResponse();
                Console.WriteLine("[HTTP] Response status: {0}", res.StatusCode);

                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)){
                  Say(sr.ReadToEnd());
                }
            }
            catch (WebException ex){
                Console.WriteLine("[HTTP] Exception: " + ex.Message);
            }
        }

        // -----------------------------------------
        //  WSRMacro SPEECH
        // -----------------------------------------

        public void Say(String tts) {
            if (tts == null) { return; }

            Console.WriteLine("[TTS] Say: {0}", tts);
            using (SpeechSynthesizer synthesizer = new SpeechSynthesizer()) {

                // Configure the audio output.
                synthesizer.SetOutputToDefaultAudioDevice();

                // Build and speak a prompt.
                PromptBuilder builder = new PromptBuilder();
                builder.AppendText(tts);
                synthesizer.Speak(builder);
            }
        }

        // -----------------------------------------
        //  WSRMacro LISTENERS
        // -----------------------------------------

        // Handle the LoadGrammarCompleted event.
        protected void recognizer_LoadGrammarCompleted(object sender, LoadGrammarCompletedEventArgs e){
            Console.WriteLine("[Grammar]  Loaded: " + e.Grammar.Name);
            this.loading--;
            StartRecognizer();
        }

        // Handle the SpeechRecognized event.
        protected void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
            RecognitionResult rr = e.Result;

            // 1. Handle dictation mode
            if (this.dication.Enabled) {
                if (rr.Confidence < CONFIDENCE_DICTATION) {
                    Console.WriteLine("[Engine] Dictation rejected: " + rr.Confidence + " Text: " + rr.Text);
                    return;
                }

                Console.WriteLine("[Engine] Dictation recognized: " + rr.Confidence + " Text: " + rr.Text);

                // Stop dictation
                this.dication.Enabled = false;

                // Send previous request with dication
                String dication = System.Uri.EscapeDataString(rr.Text);
                SendRequest(this.dictationUrl + "&dictation=" + dication);

                this.dictationUrl = null;
                return;
            }

            // 2. Handle speech mode

            // Build XPath navigator
            XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();
            double confidence = GetResultThreashold(xnav);

            if (rr.Confidence < confidence) {
                Console.WriteLine("[Engine] Speech rejected: " + rr.Confidence + " Text: " + rr.Text);
                return;
            }

            Console.WriteLine("[Engine] Speech recognized: " + rr.Confidence + " Text: " + rr.Text);
            Console.WriteLine(xnav.OuterXml);

            // Parse Result's TTS
            String tts = GetResultTTS(xnav);
            Say(tts);

            // Parse Result's URL and send Request
            String url = GetResultURL(xnav);

            // Parse Result's Dication
            if (hasDictation(xnav)) {
                this.dictationUrl = url; 
                return;
            }

            // Otherwise send the request
            SendRequest(url);
        }

        // Handle the SpeechRecognized event.
        protected void recognizer_RecognizeCompleted(object sender, RecognizeCompletedEventArgs e) {

            string resultText = e.Result != null ? e.Result.Text : "<null>";
            Console.WriteLine("[Engine] RecognizeCompleted ({0}): {1}", DateTime.Now.ToString("mm:ss.f"), resultText);
         // Console.WriteLine("[Engine]  BabbleTimeout: {0}; InitialSilenceTimeout: {1}; Result text: {2}", e.BabbleTimeout, e.InitialSilenceTimeout, resultText);
            this.start = false;
            StartRecognizer();
        }

        // Handle Audio state changed event
        static void recognizer_AudioStateChanged(object sender, AudioStateChangedEventArgs e) {
            // Console.WriteLine("[Engine] AudioStateChanged ({0}): {1}", DateTime.Now.ToString("mm:ss.f"), e.AudioState);
        }

        // Handle Grammar change event
        protected void watcher_Changed(object sender, FileSystemEventArgs e) {
            SetGrammar(this.directory);
        }
    }
}