﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using CameraControl.Devices.Classes;
using PortableDeviceLib;
using Timer = System.Timers.Timer;

namespace CameraControl.Devices.Sony
{
    public class SonyWifiCamera : BaseCameraDevice
    {
        private LiveViewData _liveViewData = new LiveViewData();
        private bool _shoulStopLiveView;


        [StructLayout(LayoutKind.Sequential)]
        struct CommonHeader
        {
            public byte StartByte;
            public byte Type;
            public Int16 SequenceNo;
            public Int32 TimeStamp;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct PayloadHeader
        {
            public Int32 StartCode;
            public UInt16 JpgDataSize;
            //public byte Dummy;
            public byte PadingSize;
            public Int32 Reserved;
            public byte Flag;
        }

        private static int request_id = 0;
        public string EndPoint { get; set; }
        private readonly HttpClient mClient = new HttpClient();
        List<string> AvailableMethods;
        private Timer _timer = new Timer(100);
        private string _liveViewUrl = "";
        private int _lastZoomPos = 0;
        public SonyWifiCamera()
        {

        }

        public bool Init(string endpoint)
        {
            Capabilities.Add(CapabilityEnum.LiveView);
            _timer.Elapsed += _timer_Elapsed;
            _timer.AutoReset = true;
            IsBusy = true;
            EndPoint = endpoint;
            AvailableMethods = GetMethodTypes();
            ExecuteMethod("startRecMode");
            IsConnected = true;
            ExposureMeteringMode = new PropertyValue<int>();
            ExposureMeteringMode.Available = false;
            LiveViewImageZoomRatio = new PropertyValue<int>();
            for (int i = 0; i < 101; i++)
            {
                LiveViewImageZoomRatio.AddValues(i.ToString(), i);
            }
            LiveViewImageZoomRatio.ReloadValues();
            LiveViewImageZoomRatio.ValueChanged += LiveViewImageZoomRatio_ValueChanged;
            Task.Factory.StartNew(InitProps);
            return true;
        }

        void LiveViewImageZoomRatio_ValueChanged(object sender, string key, int val)
        {
            if (val > _lastZoomPos)
            {
                //ExecuteMethod("actZoom", "out", "1shot");
                ExecuteMethod("actZoom", "out", "start");
            }
            else
            {
                //ExecuteMethod("actZoom", "in", "1shot");
                ExecuteMethod("actZoom", "in", "start");
            }
            _lastZoomPos = val;
        }

        private void InitProps()
        {
            Thread.Sleep(3500);
            try
            {
                InitIso();
                InitShutterSpeed();
                InitFNumber();
                InitFocusMode();
                InitWhitebalce();
                InitCompressionSetting();
                InitMode();
            }
            catch (Exception ex)
            {
                Log.Error("Sony prop init error", ex);
            }
            IsBusy = false;
            _timer.Start();
        }

        private void InitIso()
        {
            IsoNumber=new PropertyValue<int>();
            var prop = IsoNumber;
            var cap = AsCapability<string>(Post(CreateJson("getAvailableIsoSpeedRate")));
            SetCapability(prop, cap);
            prop.ValueChanged += (sender, key, val) => CheckError(Post(CreateJson("setIsoSpeedRate", "1.0", key)));
        }

        private void InitFNumber()
        {
            FNumber = new PropertyValue<int>();
            var prop = FNumber;
            var cap = AsCapability<string>(Post(CreateJson("getAvailableFNumber")));
            SetCapability(prop, cap);
            prop.ValueChanged += (sender, key, val) => CheckError(Post(CreateJson("setFNumber", "1.0", key)));
        }

        private void InitShutterSpeed()
        {
            ShutterSpeed = new PropertyValue<long>();
            var prop = ShutterSpeed;
            var cap = AsCapability<string>(Post(CreateJson("getAvailableShutterSpeed")));
            SetCapability(prop, cap);
            prop.ValueChanged += (sender, key, val) => CheckError(Post(CreateJson("setShutterSpeed", "1.0", key)));
        }

        private void InitFocusMode()
        {
            FocusMode = new PropertyValue<long>();
            //var prop = FocusMode;
            //var cap = AsCapability<string>(Post(CreateJson("getAvailableFocusMode")));
            //SetCapability(prop, cap);
            //prop.ValueChanged += (sender, key, val) => CheckError(Post(CreateJson("setFocusMode", "1.0", key)));
        }

        private void InitWhitebalce()
        {
            //TODO:fix me
            WhiteBalance = new PropertyValue<long>();
            try
            {
                var prop = WhiteBalance;
                var cap = AsCapability<string>(Post(CreateJson("getAvailableWhiteBalance")));
                SetCapability(prop, cap);
                prop.ValueChanged += (sender, key, val) => CheckError(Post(CreateJson("setWhiteBalance", "1.0", key)));
            }
            catch (Exception)
            {
                WhiteBalance.Available = false;
            }
        }

        private void InitCompressionSetting()
        {
            CompressionSetting = new PropertyValue<int>();
            try
            {
                var prop = CompressionSetting;
                var cap = AsCapability<string>(Post(CreateJson("getAvailableStillQuality")));
                SetCapability(prop, cap);
                prop.ValueChanged += (sender, key, val) => CheckError(Post(CreateJson("setStillQuality", "1.0", key)));
            }
            catch (Exception)
            {
                CompressionSetting.Available = false;
            }
        }

        private void InitMode()
        {
            try
            {
                Mode = new PropertyValue<uint>();
                var prop = Mode;
                var cap = AsCapability<string>(Post(CreateJson("getAvailableExposureMode")));
                SetCapability(prop, cap);
                prop.ValueChanged += (sender, key, val) => CheckError(Post(CreateJson("setExposureMode", "1.0", key)));
            }
            catch (Exception)
            {
                
            }
        }

        void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _timer.Stop();
            Task.Factory.StartNew(GetEvent);
        }

        private void GetEvent()
        {
            try
            {
                var jString = Post(CreateJson("getEvent","1.0",false));
                var json = Initialize(jString);
                var jResult = json["result"] as JArray;

                if (jResult == null) return;
                var elem = jResult[2];
                if (elem.HasValues)
                {
                    LiveViewImageZoomRatio.SetValue(elem.Value<int>("zoomPositionCurrentBox").ToString(), false);
                }
                
                elem = jResult[18];
                if (elem.HasValues)
                {
                    SetCapability(Mode, new Capability<string>
                    {
                        Current = elem.Value<string>("currentExposureMode"),
                        Candidates = elem["exposureModeCandidates"].Values<string>().ToList()
                    });
                }

                elem = jResult[27];
                if (elem.HasValues)
                {
                    SetCapability(FNumber, new Capability<string>
                    {
                        Current = elem.Value<string>("currentFNumber"),
                        Candidates = elem["fNumberCandidates"].Values<string>().ToList()
                    });
                }
                elem = jResult[28];
                if (elem.HasValues)
                {
                    SetCapability(FocusMode, new Capability<string>
                    {
                        Current = elem.Value<string>("currentFocusMode"),
                        Candidates = elem["focusModeCandidates"].Values<string>().ToList()
                    });
                }

                elem = jResult[29];
                if (elem.HasValues)
                {
                    SetCapability(IsoNumber, new Capability<string>
                    {
                        Current = elem.Value<string>("currentIsoSpeedRate"),
                        Candidates = elem["isoSpeedRateCandidates"].Values<string>().ToList()
                    });
                }
                elem = jResult[32];
                if (elem.HasValues)
                {
                    SetCapability(ShutterSpeed, new Capability<string>
                    {
                        Current = elem.Value<string>("currentShutterSpeed"),
                        Candidates = elem["shutterSpeedCandidates"].Values<string>().ToList()
                    });
                }
                elem = jResult[33];
                if (elem.HasValues)
                {
                    WhiteBalance.SetValue(elem.Value<string>("currentWhiteBalanceMode"), false);
                }
                if (jResult.Count > 36) // GetEvent version 1.2
                {
                    elem = jResult[37];
                    if (elem.HasValues)
                    {
                        SetCapability(CompressionSetting, new Capability<string>
                        {
                            Current = elem.Value<string>("stillQuality"),
                            Candidates = elem["candidate"].Values<string>().ToList()
                        });
                    }
                }
            }
            catch (Exception)
            {
                
            }
            _timer.Start();
        }

        public override void CapturePhoto()
        {
            IsBusy = true;
            var url = AsPrimitiveList<string>(Post(CreateJson("actTakePicture")))[0];
            if (url.Contains("?"))
                url = url.Split('?')[0];
            PhotoCapturedEventArgs args = new PhotoCapturedEventArgs
            {
                WiaImageItem = null,
                EventArgs = new PortableDeviceEventArgs(),
                CameraDevice = this,
                FileName = url.Replace('/', '\\'),
                Handle = url
            };
            OnPhotoCapture(this, args);
        }

        public override void StartLiveView()
        {
            _shoulStopLiveView = false;
            _liveViewUrl = AsPrimitive<string>(Post(CreateJson("startLiveview")));
            BeginGetStream(new Uri(_liveViewUrl));
        }

        public override void StopLiveView()
        {
            _shoulStopLiveView = true;
            Post(CreateJson("stopLiveview"));
        }

        public override LiveViewData GetLiveViewImage()
        {
            return _liveViewData;
        }

        public override void TransferFile(object o, string filename)
        {
            TransferProgress = 0;
            HttpHelper.DownLoadFileByWebRequest(((string)o) + "?size=Origin", filename, this);
        }

        public override void TransferFileThumb(object o, string filename)
        {
            TransferProgress = 0;
            HttpHelper.DownLoadFileByWebRequest(((string)o) + "?size=Scn", filename,
                this);
        }

        private static void SetCapability<T>(PropertyValue<T> prop, Capability<string> cap)
        {
            // refresh properties only if the collection or value was changed 
            if (prop.Value == cap.Current && prop.Values.Count == cap.Candidates.Count)
                return;
            //prop.Clear(false);
            foreach (string val in cap.Candidates)
            {
                prop.AddValues(val, default(T));
            }

            if (cap.Candidates.Count == 0 && !string.IsNullOrEmpty(cap.Current))
            {
                prop.AddValues(cap.Current, default(T));
                prop.IsEnabled = false;
            }
            else
            {
                prop.IsEnabled = true;
            }
            prop.ReloadValues();
            prop.SetValue(cap.Current, false);
        }
        
        private void ExecuteMethod(string method, params object[] prms)
        {
            var res = Post(CreateJson(method, prms));
        }

        private List<string> GetMethodTypes()
        {
            var methodTypes = new List<string>();
            var res = Post(CreateJson("getMethodTypes", "1.0", ""));
            var json = Initialize(res);

            foreach (var token in json["results"])
            {
                methodTypes.Add(token.Value<string>(0));
            }
            return methodTypes;
        }

        private static string CreateJson(string name, params object[] prms)
        {
            return CreateJson(name, "1.0", prms);
        }

        private static string CreateJson(string name, string version, params object[] prms)
        {
            var param = new JArray();
            if (prms != null)
            {
                foreach (var p in prms)
                {
                    param.Add(p);
                }
            }
            var json = new JObject(
                new JProperty("method", name),
                new JProperty("version", version),
                new JProperty("id", GetID()),
                new JProperty("params", param));

            return json.ToString(Formatting.None);
        }

        private static int GetID()
        {
            var id = Interlocked.Increment(ref request_id);
            if (request_id > 1000000000)
            {
                request_id = 0;
            }
            return id;
        }

        internal string Post(string body)
        {
            if (EndPoint == null || body == null)
            {
                throw new ArgumentNullException();
            }

            var content = new StringContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            try
            {
                Task<HttpResponseMessage> task;

                var response = mClient.PostAsync(EndPoint, content).Result;
                return Encoding.UTF8.GetString(response.Content.ReadAsByteArrayAsync().Result);

                //if (response.IsSuccessStatusCode)
                //{
                //    // ReadAsString fails in case of charset=utf-8
                //    return Encoding.UTF8.GetString(response.Content.ReadAsByteArrayAsync().Result);
                //}
                //else
                //{
                //    //Debug.WriteLine("Http Status Error: " + response.StatusCode);
                //    //throw new RemoteApiException((int)response.StatusCode);
                //}
            }
            //catch (RemoteApiException e)
            //{
            //    Debug.WriteLine("Request error: " + e.Message);
            //    Debug.WriteLine("Request error: " + e.StackTrace);
            //    throw e;
            //}
            catch (Exception e)
            {
                throw e;
                //if (e is TaskCanceledException || e is OperationCanceledException)
                //{
                //    Debug.WriteLine("Request cancelled: " + e.StackTrace);
                //    throw new RemoteApiException(StatusCode.Cancelled);
                //}
                //else
                //{
                //    Debug.WriteLine("HttpPost Exception: " + e.StackTrace);
                //    throw new RemoteApiException(StatusCode.NetworkError);
                //}
            }
        }

        internal static void CheckError(string jString)
        {
            var json = Initialize(jString);
        }

        internal static JObject Initialize(string response)
        {
            var json = JObject.Parse(response);
            if (json == null)
            {
                //throw new RemoteApiException(StatusCode.IllegalResponse);
            }
            if (json["error"] != null)
            {
                //throw new RemoteApiException((StatusCode)json["error"].Value<int>(0));
            }
            return json;
        }

        /// <summary>
        /// For response which has a single Array consists of a single type.
        /// </summary>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <param name="jString"></param>
        internal static List<T> AsPrimitiveList<T>(string jString)
        {
            var json = Initialize(jString);

            return json["result"][0].Values<T>().ToList();
        }

        internal static T AsPrimitive<T>(string jString)
        {
            var json = Initialize(jString);

            return json["result"].Value<T>(0);
        }

        internal static Capability<T> AsCapability<T>(string jString)
        {
            var json = Initialize(jString);

            return new Capability<T>
            {
                Current = json["result"].Value<T>(0),
                Candidates = json["result"][1].Values<T>().ToList()
            };
        }

        public void BeginGetStream(Uri uri)
        {
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(uri);

            // asynchronously get a response
            request.BeginGetResponse(OnGetResponse, request);
        }

        private void OnGetResponse(IAsyncResult asyncResult)
        {
            // get the response
            HttpWebRequest req = (HttpWebRequest)asyncResult.AsyncState;

            try
            {
                HttpWebResponse resp = (HttpWebResponse)req.EndGetResponse(asyncResult);
                Stream s = resp.GetResponseStream();


                while (true)
                {
                    object heder = new CommonHeader();
                    ByteArrayToStructure(s, ref heder);
                    if (((CommonHeader) heder).Type == 0x12)
                    {
                        
                    }
                    PayloadHeader playload = new PayloadHeader();
                    //ByteArrayToStructure(s, ref playload);
                    var buff = ReadBytes(s, 128);

                    playload.StartCode = BitConverter.ToInt32(buff, 0);
                    playload.JpgDataSize = BitConverter.ToUInt16(new byte[] { buff[6], buff[5], buff[4], 0 }, 0);
                    playload.PadingSize = buff[7];
                    _liveViewData.ImageData = ReadBytes(s, playload.JpgDataSize);

                    if (playload.PadingSize > 0)
                        ReadBytes(s, playload.PadingSize);
                    if (_shoulStopLiveView)
                        break;
                }
                resp.Close();
            }
            catch (Exception ex)
            {
                Log.Error("Unable to download stream ", ex);
            }
        }

        void ByteArrayToStructure(Stream br, ref object obj)
        {
            int len = Marshal.SizeOf(obj);
            var bytearray = ReadBytes(br, len);
            IntPtr i = Marshal.AllocHGlobal(len);
            Marshal.Copy(bytearray, 0, i, len);
            obj = Marshal.PtrToStructure(i, obj.GetType());
            Marshal.FreeHGlobal(i);
        }

        private byte[] ReadBytes(Stream s, int length)
        {
            var payload = new byte[length];
            int numBytes = 0;
            while (numBytes != length)
            {
                numBytes += s.Read(payload, numBytes, length - numBytes);
            }
            return payload;
        }
    }
}