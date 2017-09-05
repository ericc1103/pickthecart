using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.Devices.PointOfService;
using System.Diagnostics;
using Windows.Storage.Streams;
using System.Text;
using Windows.Devices.Enumeration;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.UI.Popups;

namespace App1
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private DispatcherTimer clockTimer;
        private DispatcherTimer barcodeScannerTimer;
        private string currentTime;
        private Brush sectionABackgroundColor;
        private Brush sectionABorderColor;
        private Brush sectionBBackgroundColor;
        private Brush sectionBBorderColor;
        private Brush sectionCBorderColor;
        private Brush sectionDBackgroundColor;
        private Brush sectionDBorderColor;
        private string tempBarcode;
        private CurrentState currentState;

        // Define colors.
        private readonly Brush BlueBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 65, 105, 226));
        private readonly Brush BlueBorderBrush = new SolidColorBrush(Color.FromArgb(255, 136, 205, 249));
        private readonly Brush RedBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 254, 0, 0));
        private readonly Brush RedBorderBrush = new SolidColorBrush(Color.FromArgb(255, 253, 122, 109));
        private readonly Brush GrayBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
        private readonly Brush GrayBorderBrush = new SolidColorBrush(Color.FromArgb(255, 105, 105, 105));
        private readonly Brush GreenBorderBrush = new SolidColorBrush(Color.FromArgb(255, 144, 238, 144));

        private const string BibleTeachCode = "5QCPCM0QW";
        private const string BoxLabelCode = "123456";
        private const char RETURN = '\r';

        [DllImport("user32.dll")]
        private static extern int MapVirtualKey(uint uCode, uint uMapType);

        // Barcode Scanner.
        private BarcodeScanner scanner = null;
        private ClaimedBarcodeScanner claimedScanner = null;

        public static DeviceWatcher watcher = null;

        public MainPage()
        {   
            this.InitializeComponent();

            this.currentState = CurrentState.New;
            this.DataContext = this;

            var window = Window.Current;
            window.Content.KeyDown += OnKeyDown;            

            this.SetupBarcodeScannerTimer();
            this.SetupClockTimer();
            this.SetCurrentState(this.currentState).ConfigureAwait(false);
            this.InitializeBarcodeScanner().ConfigureAwait(false);
        }


        private char ConvertVirtualKeyToChar(VirtualKey virtualKey)
        {
            // 2 is used to translate into an unshifted character value 
            int nonVirtualKey = MapVirtualKey((uint)virtualKey, 2);

            return Convert.ToChar(nonVirtualKey);
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                this.SetCurrentState(CurrentState.New).ConfigureAwait(false);
                return;
            }

            this.barcodeScannerTimer.Start();

            var keyChar = this.ConvertVirtualKeyToChar(e.Key);
            if (keyChar == RETURN)
            {
                this.Reset();

                if (this.currentState == CurrentState.New || this.currentState == CurrentState.Invalid)
                {
                    if (this.Barcode == BibleTeachCode)
                    {
                        this.SetCurrentState(CurrentState.Success).ConfigureAwait(false);
                    }
                    else
                    {
                        this.SetCurrentState(CurrentState.Invalid).ConfigureAwait(false);
                    }

                }
                else if (this.currentState == CurrentState.Success || this.currentState == CurrentState.SuccessInvalid)
                {
                    if (this.Barcode == BoxLabelCode)
                    {
                        this.SetCurrentState(CurrentState.Completed).ConfigureAwait(false);
                    }
                    else
                    {
                        this.SetCurrentState(CurrentState.SuccessInvalid).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                this.tempBarcode += keyChar.ToString();
            }
        }

        private void Reset(bool isStartOver = false)
        {
            this.barcodeScannerTimer.Stop();
            this.Barcode = isStartOver ? string.Empty : this.GetCodeFromRawString(this.tempBarcode);
            Debug.WriteLine(this.Barcode);
            this.tempBarcode = string.Empty;
        }

        /// <summary>
        /// Sets up the barcode scanner to be ready to receive the data events from the scan.
        /// </summary>
        private async Task InitializeBarcodeScanner()
        {
            // create the barcode scanner. 
            if (await CreateDefaultScannerObject())
            {
                // after successful creation, claim the scanner for exclusive use and enable it so that data reveived events are received.
                if (await ClaimScanner())
                {

                    // It is always a good idea to have a release device requested event handler. If this event is not handled, there are chances of another app can 
                    // claim ownsership of the barcode scanner.
                    this.claimedScanner.ReleaseDeviceRequested += this.ClaimedScanner_ReleaseDeviceRequested;                                       

                    // after successfully claiming, attach the datareceived event handler.
                    this.claimedScanner.DataReceived += this.ClaimedScanner_DataReceived;

                    // Ask the API to decode the data by default. By setting this, API will decode the raw data from the barcode scanner and 
                    // send the ScanDataLabel and ScanDataType in the DataReceived event
                    this.claimedScanner.IsDecodeDataEnabled = true;

                    // enable the scanner.
                    // Note: If the scanner is not enabled (i.e. EnableAsync not called), attaching the event handler will not be any useful because the API will not fire the event 
                    // if the claimedScanner has not beed Enabled
                    await this.claimedScanner.EnableAsync();
                }
            }
            else
            {
                Debug.Fail("No Barcode Scanner found.");
            }
        }

        /// <summary>
        /// Event handler for the Release Device Requested event fired when barcode scanner receives Claim request from another application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"> Contains the ClamiedBarcodeScanner that is sending this request</param>
        async void ClaimedScanner_ReleaseDeviceRequested(object sender, ClaimedBarcodeScanner e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                // alsways retain the device
                e.RetainDevice();
            });
        }

        /// <summary>
        /// Event handler for the DataReceived event fired when a barcode is scanned by the barcode scanner 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"> Contains the BarcodeScannerReport which contains the data obtained in the scan</param>
        async void ClaimedScanner_DataReceived(ClaimedBarcodeScanner sender, BarcodeScannerDataReceivedEventArgs args)
        {
            // need to update the UI data on the dispatcher thread.
            // update the UI with the data received from the scan.
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                // read the data from the buffer and convert to string.
                var a = GetDataLabelString(args.Report.ScanDataLabel, args.Report.ScanDataType);

                var b = this.GetDataString(args.Report.ScanData);

                var c = BarcodeSymbologies.GetName(args.Report.ScanDataType);
            });
        }

        string GetDataLabelString(IBuffer data, uint scanDataType)
        {
            string result = null;
            // Only certain data types contain encoded text.
            //   To keep this simple, we'll just decode a few of them.
            if (data == null)
            {
                result = "No data";
            }
            else
            {
                switch (BarcodeSymbologies.GetName(scanDataType))
                {
                    case "Upca":
                    case "UpcaAdd2":
                    case "UpcaAdd5":
                    case "Upce":
                    case "UpceAdd2":
                    case "UpceAdd5":
                    case "Ean8":
                    case "TfStd":
                        // The UPC, EAN8, and 2 of 5 families encode the digits 0..9
                        // which are then sent to the app in a UTF8 string (like "01234")

                        // This is not an exhaustive list of symbologies that can be converted to a string

                        DataReader reader = DataReader.FromBuffer(data);
                        result = reader.ReadString(data.Length);
                        break;
                    default:
                        // Some other symbologies (typically 2-D symbologies) contain binary data that
                        //  should not be converted to text.
                        result = string.Format("Decoded data unavailable. Raw label data: {0}", GetDataString(data));
                        break;
                }
            }

            return result;
        }

        string GetDataString(IBuffer data)
        {
            StringBuilder result = new StringBuilder();

            if (data == null)
            {
                result.Append("No data");
            }
            else
            {
                // Just to show that we have the raw data, we'll print the value of the bytes.
                // Arbitrarily limit the number of bytes printed to 20 so the UI isn't overloaded.
                const uint MAX_BYTES_TO_PRINT = 20;
                uint bytesToPrint = Math.Min(data.Length, MAX_BYTES_TO_PRINT);

                DataReader reader = DataReader.FromBuffer(data);
                byte[] dataBytes = new byte[bytesToPrint];
                reader.ReadBytes(dataBytes);

                for (uint byteIndex = 0; byteIndex < bytesToPrint; ++byteIndex)
                {
                    result.AppendFormat("{0:X2} ", dataBytes[byteIndex]);
                }

                if (bytesToPrint < data.Length)
                {
                    result.Append("...");
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Creates the default barcode scanner.
        /// </summary>
        /// <returns>true if barcode scanner is created. Otherwise returns false</returns>
        private async Task<bool> CreateDefaultScannerObject()
        {
            if (scanner == null)
            {
                scanner = await BarcodeScanner.GetDefaultAsync();

                if (scanner == null)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Reset the Scenario state
        /// </summary>
        private void ResetBarcodeScanner()
        {
            if (this.claimedScanner != null)
            {
                // Detach the event handlers
                this.claimedScanner.DataReceived -= ClaimedScanner_DataReceived;
                this.claimedScanner.ReleaseDeviceRequested -= ClaimedScanner_ReleaseDeviceRequested;
                // Release the Barcode Scanner and set to null
                this.claimedScanner.Dispose();
                this.claimedScanner = null;
            }

            scanner = null;
        }

        /// <summary>
        /// This method claims the barcode scanner 
        /// </summary>
        /// <returns>true if claim is successful. Otherwise returns false</returns>
        private async Task<bool> ClaimScanner()
        {
            if (this.claimedScanner == null)
            {
                // claim the barcode scanner
                this.claimedScanner = await scanner.ClaimScannerAsync();

                // enable the claimed barcode scanner
                if (this.claimedScanner == null)
                {
                    return false;
                }
            }

            return true;
        }

        public string Barcode { get; set; }

        public string CurrentTime
        {
            get
            {
                return this.currentTime;
            }
            set
            {
                this.currentTime = value;
                this.NotifyPropertyChanged("CurrentTime");
            }
        }

        public Brush SectionABackgroundColor
        {
            get
            {
                return this.sectionABackgroundColor;
            }
            set
            {
                this.sectionABackgroundColor = value;
                this.NotifyPropertyChanged("SectionABackgroundColor");
            }
        }

        public Brush SectionABorderColor
        {
            get
            {
                return this.sectionABorderColor;
            }
            set
            {
                this.sectionABorderColor = value;
                this.NotifyPropertyChanged("SectionABorderColor");
            }
        }

        public Brush SectionBBackgroundColor
        {
            get
            {
                return this.sectionBBackgroundColor;
            }
            set
            {
                this.sectionBBackgroundColor = value;
                this.NotifyPropertyChanged("SectionBBackgroundColor");
            }
        }

        public Brush SectionBBorderColor
        {
            get
            {
                return this.sectionBBorderColor;
            }
            set
            {
                this.sectionBBorderColor = value;
                this.NotifyPropertyChanged("SectionBBorderColor");
            }
        }

        public Brush SectionCBorderColor
        {
            get
            {
                return this.sectionCBorderColor;
            }
            set
            {
                this.sectionCBorderColor = value;
                this.NotifyPropertyChanged("SectionCBorderColor");
            }
        }

        public Brush SectionDBackgroundColor
        {
            get
            {
                return this.sectionDBackgroundColor;
            }
            set
            {
                this.sectionDBackgroundColor = value;
                this.NotifyPropertyChanged("SectionDBackgroundColor");
            }
        }

        public Brush SectionDBorderColor
        {
            get
            {
                return this.sectionDBorderColor;
            }
            set
            {
                this.sectionDBorderColor = value;
                this.NotifyPropertyChanged("SectionDBorderColor");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void ClockTimer_Tick(object sender, object e)
        {
            this.CurrentTime = DateTime.Now.ToString("h:mm tt");            
        }

        private void BarcodeScannerTimer_Tick(object sender, object e)
        {
            this.tempBarcode = string.Empty;
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public async Task SetCurrentState(CurrentState currentState)
        {
            this.currentState = currentState;
            string audioName = "chimes-glassy.mp3";
            switch (currentState)
            {
                case CurrentState.New:
                    audioName = "arpeggio.mp3";
                    this.NewState();
                    break;
                case CurrentState.Success:
                    this.SuccessState();
                    audioName = "coins.mp3";
                    break;
                case CurrentState.SuccessInvalid:
                    this.SucessfulInvalidState();
                    audioName = "surprise-on-a-spring.mp3";
                    break;
                case CurrentState.Invalid:
                    this.InvalidState();
                    audioName = "surprise-on-a-spring.mp3";
                    break;
                case CurrentState.Completed:
                    this.SuccessState();
                    this.DisplayCompletedMessage();
                    audioName = "what-friends-are-for.mp3";
                    break;
            }

            var element = new MediaElement();
            var folder = await Package.Current.InstalledLocation.GetFolderAsync("Sounds");
            var file = await folder.GetFileAsync(audioName);
            var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            element.SetSource(stream, "");
            element.Play();
        }


        private void SetupClockTimer()
        {
            this.clockTimer = new DispatcherTimer();
            this.clockTimer.Interval = new TimeSpan(0, 0, 1);
            this.clockTimer.Tick += ClockTimer_Tick;
            this.clockTimer.Start();
        }

        private void SetupBarcodeScannerTimer()
        {
            this.barcodeScannerTimer = new DispatcherTimer();
            this.barcodeScannerTimer.Interval = new TimeSpan(0, 0, 1);
            this.barcodeScannerTimer.Tick += BarcodeScannerTimer_Tick;
        }

        private void NewState()
        {
            this.SectionABackgroundColor = this.BlueBackgroundBrush;
            this.SectionABorderColor = this.BlueBorderBrush;
            this.SectionBBackgroundColor = this.BlueBackgroundBrush;
            this.SectionBBorderColor = this.BlueBorderBrush;
            this.SectionCBorderColor = this.BlueBorderBrush;
            this.SectionDBackgroundColor = this.GrayBackgroundBrush;
            this.SectionDBorderColor = this.GrayBorderBrush;
        }

        private void SuccessState()
        {
            this.SectionABackgroundColor = this.BlueBackgroundBrush;
            this.SectionABorderColor = this.BlueBorderBrush;
            this.SectionBBackgroundColor = this.BlueBackgroundBrush;
            this.SectionBBorderColor = this.BlueBorderBrush;
            this.SectionCBorderColor = this.GreenBorderBrush;
            this.SectionDBackgroundColor = this.BlueBackgroundBrush;
            this.SectionDBorderColor = this.BlueBorderBrush;
        }

        private void InvalidState()
        {
            this.SectionABackgroundColor = this.RedBackgroundBrush;
            this.SectionABorderColor = this.RedBorderBrush;
            this.SectionBBackgroundColor = this.RedBackgroundBrush;
            this.SectionBBorderColor = this.RedBorderBrush;
            this.SectionCBorderColor = this.RedBorderBrush;
            this.SectionDBackgroundColor = this.GrayBackgroundBrush;
            this.SectionDBorderColor = this.GrayBorderBrush;
        }

        private void SucessfulInvalidState()
        {
            this.SectionABackgroundColor = this.BlueBackgroundBrush;
            this.SectionABorderColor = this.BlueBorderBrush;
            this.SectionBBackgroundColor = this.RedBackgroundBrush;
            this.SectionBBorderColor = this.RedBorderBrush;
            this.SectionCBorderColor = this.GreenBorderBrush;
            this.SectionDBackgroundColor = this.RedBackgroundBrush;
            this.SectionDBorderColor = this.RedBorderBrush;
        }

        private string GetCodeFromRawString(string rawString)
        {
            rawString = rawString.Replace("\0;", ":");
            rawString = rawString.Replace("\03", "#");
            rawString = rawString.Replace("\0", "");
            
            if (string.IsNullOrWhiteSpace(rawString))
            {
                return null;
            }

            var parsedString = rawString.Split('#');
            if (parsedString.Length == 2)
            {
                return parsedString[1];
            }

            return parsedString[0];
        }

        private async void DisplayCompletedMessage()
        {
            // Create the message dialog and set its content
            var messageDialog = new MessageDialog("Picking Simulation is Completed.");

            messageDialog.Commands.Add(new UICommand(
                "Close",
                new UICommandInvokedHandler(this.CommandInvokedHandler)));

            // Set the command that will be invoked by default
            messageDialog.DefaultCommandIndex = 0;

            // Set the command to be invoked when escape is pressed
            messageDialog.CancelCommandIndex = 1;

            // Show the message dialog
            await messageDialog.ShowAsync();
        }

        private void CommandInvokedHandler(IUICommand command)
        {
            this.SetCurrentState(CurrentState.New).ConfigureAwait(false);
        }

        public enum CurrentState
        {
            New,
            Invalid,
            Success,
            SuccessInvalid,
            Completed
        }
    }
}
