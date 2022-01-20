/*****************************************************************************/
/* Project  : LedMatrix                                                      */
/* File     : DisplayForm.cs                                                 */
/* Version  : 1                                                              */
/* Language : C#                                                             */
/* Summary  : Implementation of the demonstration interface                  */
/* Creation : 23/02/2010                                                     */
/* Autor    : Guillaume CHOUTEAU                                             */
/* History  :                                                                */
/*****************************************************************************/
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using LedMatrixControlNamespace;
using LST_Busline.Settings;
using Newtonsoft.Json;
using NonInvasiveKeyboardHookLibrary;
using UpdaterService.Diagnostics.Update;


namespace LSTBusline
{
    /// <summary>
    /// Definition of the demonstration interface
    /// </summary>
    public partial class DisplayForm : Form
    {
        #region Thread-Safe Status + Progressbar
        private delegate void SafeSetTextBoxText(string text);
        private delegate void SafeSetProgressBarValue(int value);

        private void SplitAndUpdateStatusTextAndProgressbar(string InputText)
        {
            String[] SplitUp = InputText.Split('#');

            if (SplitUp.Length > 1)
            {
                WriteTextSafe(SplitUp[0]);
                SetProgressBarValueSafe(Convert.ToInt16(SplitUp[1]));
            }


        }
        private void WriteTextSafe(string text)
        {
            if (UpdateStatusTextbox.InvokeRequired)
            {
                var d = new SafeSetTextBoxText(WriteTextSafe);
                UpdateStatusTextbox.Invoke(d, new object[] { text });
            }
            else
            {
                UpdateStatusTextbox.Text = text;
            }
        }
        private void SetProgressBarValueSafe(int value)
        {
            /*if (SplashProgressBar.InvokeRequired)
            {
                var d = new SafeSetProgressBarValue(SetProgressBarValueSafe);
                SplashProgressBar.Invoke(d, new object[] { value });
            }
            else
            {
                SplashProgressBar.Value = value;
            }*/
        }
        #endregion



        int idText_Line1;     
        int idText_Line2;
        int idLogo;
        int idCurrentTime;
        int idNextStopTime;
        bool even_or_uneven = false; // Hilfsvariable um regelm�ssig zwischen zwei Zust�nden hin und her zu schalten
        DateTime lastrun = DateTime.Now;
        bool m_bIsOn;   
        bool m_bIsHide;
        KeyboardHookManager _kManager;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Timer timer2;

        String BusLine =  "###############";
        String NextStop = "###############";
        int MaxLineCharacters = 20;

        int currentMode = 0; // aktueller Modus
        int currentLine = 0; // aktuelle Linie im aktuellen Modus
        int currentStop = 0; // aktueller Halt der aktuellen Linie im aktuellen Modus

        bool invalidated = false;

        SettingsRoot Konfiguration;
        //-------------------------------//
        //         CONSTRUCTOR           //
        //-------------------------------//
        #region Constuctor

        /// <summary>
        /// Class constructor
        /// </summary>
        public DisplayForm()
        {
            InitializeComponent();
            SplitAndUpdateStatusTextAndProgressbar("Starte...#0");

            Log.Event += (sender, e) => SplitAndUpdateStatusTextAndProgressbar(e.Message);


            #region read configuration
            Konfiguration = ReadConfiguration();
            WriteConfiguration(Konfiguration);
            #endregion

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            versionlabel.Text = String.Format("{0}", version);

            ledMatrixControl.LedOnColor = Color.FromArgb(Konfiguration?.settings?.style?.led_on_color ?? -256);
            ledMatrixControl.LedOffColor = Color.FromArgb(Konfiguration?.settings?.style?.led_off_color ?? -12566464);
            ledMatrixControl.SizeCoeff = (double)Konfiguration.settings.style.led_size_coefficient;
            if (Konfiguration.settings.style?.ledtype == 0)
            {
                ledMatrixControl.SetLedStyle(LedSyle.Round);
                cbxDisplayLedStyle.SelectedIndex = 0;
            }                
            else
            {
                ledMatrixControl.SetLedStyle(LedSyle.Square);
                cbxDisplayLedStyle.SelectedIndex = 1;
            }

            // Get the color from the control
            pbtDisplayLedOn.BackColor = ledMatrixControl.LedOnColor;
            pbtDisplayLedOff.BackColor = ledMatrixControl.LedOffColor;


            // Get the size from the control
            ledMatrixControl.SetMatrixSize(Konfiguration.settings?.style?.lines ?? 200, Konfiguration.settings?.style?.rows ?? 17);
            nudLedSize.Value = (decimal)ledMatrixControl.SizeCoeff;

            // Set the font collection
            ledMatrixControl.LoadFontCollectionFromResource("LST-Busline-Font.xml");

            // Add the text item to the conrol
            //idLogo = ledMatrixControl.AddTextItem(tbxTx1.Text, new Point(0, 0), ItemDirection.Right, ItemSpeed.Idle);
            idCurrentTime =  ledMatrixControl.AddTextItem(GenerateCurrentTimeString(), new Point(165, 0), ItemDirection.Right, ItemSpeed.Idle);
            idNextStopTime = ledMatrixControl.AddTextItem(GenerateNextStopTime(), new Point(165, 8), ItemDirection.Right, ItemSpeed.Idle);
            idLogo = ledMatrixControl.AddTextItem(GenerateLogo(), new Point(0, 0), ItemDirection.Right, ItemSpeed.Idle);

            idText_Line1 = ledMatrixControl.AddTextItem(GenerateLine1(), new Point(25, 0), ItemDirection.Right, ItemSpeed.Idle);
            idText_Line2 = ledMatrixControl.AddTextItem(GenerateLine2(), new Point(25, 8), ItemDirection.Right, ItemSpeed.Idle);

            // Line Abort Window
            LinienAbbruchZeitUpDownControl.Value = Konfiguration.settings.lineabortwindow;

            // Init the flags
            m_bIsOn = false;
            m_bIsHide = false;

            // Register Hotkeys
            _kManager = new KeyboardHookManager();
            _kManager.Start();
           
            RegisterHotkeys(_kManager);
            invalidated = true;
            InitTimer();

            var updater = new UpdaterService.Diagnostics.Update.Updater(StartUpAndUpdate.ReadResourceHelper.ReadResource("update.xml"));
            updater.StartMonitoring();
        }

        #region Konfiguration
        public SettingsRoot ReadConfiguration()
        {
            return JsonConvert.DeserializeObject<SettingsRoot>(File.ReadAllText("LST-Busline-Konfiguration.json")) ?? new SettingsRoot();
        }

        public void WriteConfiguration(SettingsRoot Konfiguration)
        {
            // write it
            File.WriteAllText("LST-Busline-Konfiguration.json", JsonConvert.SerializeObject(Konfiguration, Formatting.Indented));

        }
        #endregion

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Fensterposition wiederherstellen
            Location = new Point(Konfiguration.settings.style.windowposition_x, Konfiguration.settings.style.windowposition_y);
            Size = new Size(Konfiguration.settings.style.windowwidth, Konfiguration.settings.style.windowheight);

        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // Fensterposition speichern...
            Konfiguration.settings.style.windowposition_x = Location.X;
            Konfiguration.settings.style.windowposition_y = Location.Y;
            Konfiguration.settings.style.windowheight = Size.Height;
            Konfiguration.settings.style.windowwidth = Size.Width;
            WriteConfiguration(Konfiguration);
        }

        public void RegisterHotkeys(KeyboardHookManager kManager)
        {

            #region register hotkeys
            //Boolean NextStop        = RegisterHotKey(this.Handle, 1, 0x0000, Konfiguration.settings.hotkeys.nextstop);
            //    if (!NextStop) { MessageBox.Show("N�chster Halt Hotkey konnte nicht aktiviert werden!"); }
            //Boolean PreviousStop    = RegisterHotKey(this.Handle, 2, 0x0000, Konfiguration.settings.hotkeys.previousstop);
            //    if (!PreviousStop) { MessageBox.Show("Vorheriger Halt Hotkey konnte nicht aktiviert werden!"); }
            //Boolean ResetLine       = RegisterHotKey(this.Handle, 3, 0x0000, Konfiguration.settings.hotkeys.resetline);
            //    if (!ResetLine) { MessageBox.Show("Reset Hotkey konnte nicht aktiviert werden!"); }
            //Boolean SwitchLine      = RegisterHotKey(this.Handle, 4, 0x0000, Konfiguration.settings.hotkeys.switchline);
            //    if (!SwitchLine) { MessageBox.Show("Linienwechsel Hotkey konnte nicht aktiviert werden!"); }
            //Boolean SwitchMode      = RegisterHotKey(this.Handle, 5, 0x0000, Konfiguration.settings.hotkeys.switchmode);
            //    if (!SwitchMode) { MessageBox.Show("Moduswechsel Hotkey konnte nicht aktiviert werden!"); }
            kManager.Stop();
            kManager.Start();
            kManager.UnregisterAll();

            // N�chster Halt
            kManager.RegisterHotkey(Konfiguration.settings.hotkeys?.nextstop ?? (int)Keys.D9, () =>
            {
                HandleHotKey_NextStop();
            });
            // Vorheriger Halt
            kManager.RegisterHotkey(Konfiguration.settings.hotkeys?.previousstop ?? (int)Keys.D8, () =>
            {
                HandleHotKey_PreviousStop();
            });
            // Reset
            kManager.RegisterHotkey(Konfiguration.settings.hotkeys?.resetline ?? (int)Keys.D7, () =>
            {
                HandleHotKey_Reset();
            });
            // Linienwechsel
            kManager.RegisterHotkey(Konfiguration.settings.hotkeys?.switchline ?? (int)Keys.D6, () =>
            {
                HandleHotKey_LineChange();
            });
            // Moduswechsel
            kManager.RegisterHotkey(Konfiguration.settings.hotkeys?.switchmode ?? (int)Keys.D5, () =>
            {
                HandleHotKey_ModeChange();
            });
            #endregion

        }

        #endregion

        #region Hotkeys behandeln und reagieren
        protected void HandleHotKey_NextStop()
        {
            Debug.WriteLine("N�chster Halt");            

            if ((currentStop+1) > Konfiguration.modes[currentMode].lines[currentLine].stops.Count() - 1) {
                // hochz�hlen geht nicht, wir fangen bei 0 an
                currentStop = 0;
            } else
            {
                // eins hochz�hlen
                currentStop += 1;
            }

            invalidated = true;
        }
        protected void HandleHotKey_PreviousStop()
        {
            Debug.WriteLine("Vorheriger Halt");

            if ((currentStop - 1) < 0)
            {
                // runterz�hlen geht nicht, wir schalten auf den letzten Stop
                currentStop = Konfiguration.modes[currentMode].lines[currentLine].stops.Count() - 1;
            }
            else
            {
                // eins runterz�hlen
                currentStop -= 1;
            }
            invalidated = true;
        }
        protected void HandleHotKey_Reset()
        {
            Debug.WriteLine("Reset");
            invalidated = true;

            currentLine = 0;
            currentMode = 0;
            currentStop = 0;
            invalidated = true;
        }
        protected void HandleHotKey_LineChange()
        {
            Debug.WriteLine("Linienwechsel");

            if ((currentLine + 1) > Konfiguration.modes[currentMode].lines.Count() - 1)
            {
                // hochz�hlen geht nicht, wir fangen bei 0 an
                currentLine = 0;
            }
            else
            {
                // eins hochz�hlen
                currentLine += 1;
            }

            // reset der Stops
            currentStop = 0;
            invalidated = true;
        }

        protected void HandleHotKey_ModeChange()
        {
            Debug.WriteLine("Moduswechsel");

            if ((currentMode + 1) > Konfiguration.modes.Count() - 1)
            {
                // hochz�hlen geht nicht, wir fangen bei 0 an
                currentMode = 0;
            }
            else
            {
                // eins hochz�hlen
                currentMode += 1;
            }

            // reset der anderen Werte
            currentLine = 0;
            currentStop = 0;
            invalidated = true;

        }

        //protected override void WndProc(ref Message m)
        //{
        //    // 5. Catch when a HotKey is pressed !
        //    if (m.Msg == 0x0312)
        //    {
        //        int id = m.WParam.ToInt32();
        //        //MessageBox.Show(string.Format("Hotkey #{0} pressed", id));
        //        switch (id)
        //        {
        //            case 1:
        //                Debug.WriteLine("N�chster Halt");
        //                break;
        //            case 2:
        //                Debug.WriteLine("Vorheriger Halt");
        //                break;
        //            case 3:
        //                Debug.WriteLine("Reset");
        //                break;
        //            case 4:
        //                Debug.WriteLine("Linienwechsel");
        //                break;
        //            case 5:
        //                Debug.WriteLine("Moduswechsel");
        //                break;
        //            default:
        //                break;
        //        }
        //    }

        //    base.WndProc(ref m);
        //}
        #endregion

        #region Timer Display Update
        public void InitTimer()
        {
            timer1 = new System.Windows.Forms.Timer();
            timer1.Tick += new EventHandler(UpdateDisplayCycle);
            timer1.Interval = 100; // in miliseconds
            timer1.Start();

            timer2 = new System.Windows.Forms.Timer();
            timer2.Tick += new EventHandler(ReRegisterHotkeys);
            timer2.Interval = 10000;
            timer2.Start();
        }

        private void ReRegisterHotkeys(object sender, EventArgs e)
        {
            Debug.WriteLine("Hotkeys re-register");
            RegisterHotkeys(_kManager);
        }

        /// <summary>
        /// wird regelm�ssig aufgerufen um das Display zu aktualisieren
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UpdateDisplayCycle(object sender, EventArgs e)
        {
            //Debug.WriteLine("Tick Tock");

            // Update Uhrzeit
            ledMatrixControl.SetItemText(idCurrentTime,GenerateCurrentTimeString());

            // nur updaten wenn wir m�ssen
            if (invalidated)
            {
                // Update Line1
                ledMatrixControl.SetItemText(idText_Line1, GenerateLine1());
                // Update Line2
                ledMatrixControl.SetItemText(idText_Line2, GenerateLine2());
                // Update N�chster Halt
                ledMatrixControl.SetItemText(idNextStopTime, GenerateNextStopTime());
                
                // stop updating
                invalidated = false;
            }

            //Debug.WriteLine("Mode: " + currentMode + " Line: " + currentLine + " Stop:" + currentStop);
        }
        #endregion

        #region Dynamische Anzeigen Erzeugung
        public String GenerateCurrentTimeString()
        {
            // umschalten...
            if ((DateTime.Now - lastrun).TotalSeconds >= 1)
            {
                lastrun = DateTime.Now;

                even_or_uneven = !even_or_uneven;
            }

            if (even_or_uneven)
                return "�" + DateTime.Now.ToString("HH:mm");
            else
                return "�" + DateTime.Now.ToString("HH�mm");
        }

        public DateTime GetNearestTimeInTheFutureForMinute(int MinuteInput)
        {
            int Minute = MinuteInput + Konfiguration.settings.lineabortwindow;
            if (Minute > 60) { Minute = 60 - Minute; }

            DateTime output = new DateTime(DateTime.Now.Year, DateTime.Now.Month,DateTime.Now.Day, DateTime.Now.Hour, MinuteInput, DateTime.Now.Second);

            if (DateTime.Now.Minute > Minute)
                output = output.AddHours(1);

            return output;
        }

        public String GenerateNextStopTime()
        {
            TimeSpan TimeDistance = TimeSpan.MaxValue;
            DateTime Abfahrtzeit = DateTime.Now;

            foreach(int Abfahrtszeit in Konfiguration.modes[currentMode].lines[currentLine].stops[currentStop].leavetimes_hourly)
            {
                DateTime nearestTimeForAbfahrzeit = GetNearestTimeInTheFutureForMinute(Abfahrtszeit);
                // qualifiziert weil in der Zukunft?
                if (TimeDistance == TimeSpan.MaxValue)
                        TimeDistance = nearestTimeForAbfahrzeit-DateTime.Now;

                if (TimeDistance > nearestTimeForAbfahrzeit-DateTime.Now)
                {
                    TimeDistance = nearestTimeForAbfahrzeit - DateTime.Now;
                    Abfahrtzeit = nearestTimeForAbfahrzeit;
                }
            }

            return "��� :" + Abfahrtzeit.Minute.ToString("D2");
        }
        public String GenerateLogo()
        {
            return "�";
        }
        public String GenerateLine1()
        {
            String currentLineName = "###############"; // Standardwert wenn Fehler
            // aktuellen Modus hernehmen und die daraus selektierte Linienbezeichnung anzeigen
            try
            {
                currentLineName = Konfiguration.modes[currentMode].lines[currentLine].name;

            } catch(Exception)
            {

            }            

            return PadCenter(currentLineName, MaxLineCharacters);
        }
        public String GenerateLine2()
        {
            String nextStopName = "###############"; // Standardwert wenn Fehler
            // aktuellen Modus hernehmen und die daraus selektierte Linienbezeichnung anzeigen
            try
            {
                nextStopName = Konfiguration.modes[currentMode].lines[currentLine].stops[currentStop].name;

            }
            catch (Exception)
            {

            }

            return PadCenter(nextStopName, MaxLineCharacters);
        }
        #endregion

        #region String helper
        static string PadCenter(string text, int newWidth)
        {
            const char filler = '�';
            int length = text.Length;
            int charactersToPad = newWidth - length;
            if (charactersToPad < 0) return text;// throw new ArgumentException("New width must be greater than string length.", "newWidth");
            int padLeft = charactersToPad / 2 + charactersToPad % 2;
            //add a space to the left if the string is an odd number
            int padRight = charactersToPad / 2;

            StringBuilder resultBuilder = new StringBuilder(newWidth);
            for (int i = 0; i < padLeft; i++) resultBuilder.Insert(i, filler);
            for (int i = 0; i < length; i++) resultBuilder.Insert(i + padLeft, text[i]);
            for (int i = newWidth - padRight; i < newWidth; i++) resultBuilder.Insert(i, filler);
            return resultBuilder.ToString();
        }
        #endregion

        //-------------------------------//
        // Display properties events     //00
        //-------------------------------//
        #region Display

        private void cbxDisplayLedStyle_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbxDisplayLedStyle.SelectedIndex == 0)
            {
                ledMatrixControl.SetLedStyle(LedSyle.Round);
            }
            else if (cbxDisplayLedStyle.SelectedIndex == 1)
            {
                ledMatrixControl.SetLedStyle(LedSyle.Square);
            }
            Konfiguration.settings.style.ledtype = cbxDisplayLedStyle.SelectedIndex;              
            WriteConfiguration(Konfiguration);

        }

        private void pbtDisplayLedOn_Click(object sender, EventArgs e)
        {
            ColorDialog cdLedOnColorDlg = new ColorDialog();

            if (cdLedOnColorDlg.ShowDialog() == DialogResult.OK)
            {
                pbtDisplayLedOn.BackColor = cdLedOnColorDlg.Color;
                ledMatrixControl.LedOnColor = cdLedOnColorDlg.Color;
            }
            Konfiguration.settings.style.led_on_color = cdLedOnColorDlg.Color.ToArgb();
            WriteConfiguration(Konfiguration);

        }

        private void pbtDisplayLedOff_Click(object sender, EventArgs e)
        {
            ColorDialog cdLedOffColorDlg = new ColorDialog();

            if (cdLedOffColorDlg.ShowDialog() == DialogResult.OK)
            {
                pbtDisplayLedOff.BackColor = cdLedOffColorDlg.Color;
                ledMatrixControl.LedOffColor = cdLedOffColorDlg.Color;
            }
            Konfiguration.settings.style.led_off_color = cdLedOffColorDlg.Color.ToArgb();
            WriteConfiguration(Konfiguration);

        }

        private void bptHide_Click(object sender, EventArgs e)
        {
            if (m_bIsHide == false)
            {
                groupBox3.Visible = false;
                groupBox2.Visible = false;
                groupBox1.Visible = false;
                pbtHide.Text = "Ausklappen";
                panel1.Height = 35;
                m_bIsHide = true;
            }
            else
            {
                groupBox3.Visible = true;
                groupBox2.Visible = true;
                groupBox1.Visible = true;
                pbtHide.Text = "Einklappen";
                panel1.Height = 147;
                m_bIsHide = false;
            }

        }

        private void nudLedSize_ValueChanged(object sender, EventArgs e)
        {
            ledMatrixControl.SizeCoeff = (double)nudLedSize.Value;

            Konfiguration.settings.style.led_size_coefficient = (double)nudLedSize.Value;
            WriteConfiguration(Konfiguration);

        }

        #endregion

        private void button_switchmode_Click(object sender, EventArgs e)
        {
            HandleHotKey_ModeChange();
        }

        private void button_switchline_Click(object sender, EventArgs e)
        {
            HandleHotKey_LineChange();
        }

        private void button_previousstop_Click(object sender, EventArgs e)
        {
            HandleHotKey_PreviousStop();
        }

        private void button_nextstop_Click(object sender, EventArgs e)
        {
            HandleHotKey_NextStop();
        }

        private void button_reset_Click(object sender, EventArgs e)
        {
            HandleHotKey_Reset();
        }

        private void Lizenzen_Click(object sender, EventArgs e)
        {
            var myForm = new ReleaseNotesCopyrightsAndLicenses();
            myForm.Show();
        }

        private void LinienAbbruchZeitUpDownControl_ValueChanged(object sender, EventArgs e)
        {
            Konfiguration.settings.lineabortwindow = Convert.ToInt32(LinienAbbruchZeitUpDownControl.Value);
            WriteConfiguration(Konfiguration);
        }

        private void toolTip1_Popup(object sender, PopupEventArgs e)
        {

        }
    }
}