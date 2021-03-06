﻿/*
 *  Copyright (C) 2017 HarpyWar <harpywar@gmail.com>
 *  
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using NLog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ChronoFagTimer
{
    public partial class TomatoForm : Form
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private Timer timeUnitTimer, tomatoShowTimer;

        /// <summary>
        /// Custom user timers
        /// </summary>
        private Dictionary<string, UserTimer> customTimers = new Dictionary<string, UserTimer>();

        int Counter
        {
            get
            {
                return _counter;
            }
            set
            {
                _counter = value;
                Logger.Trace("Counter = {0}", _counter);
            }
        }
        int _counter;


        /// <summary>
        /// Overall pomodoros for the current day
        /// </summary>
        int pomodoroCounter = 0;

        /// <summary>
        /// Current alert, show if not null
        /// </summary>
        Alert Alert = null;

        Config config;

        /// <summary>
        /// Current time index (to config.Times)
        /// </summary>
        int CurrentRound
        {
            get
            {
                if (_currentRound >= config.Times.Count)
                {
                    _currentRound = 0;
                }
                return _currentRound;
            }
            set
            {
                _currentRound = value >= 0 
                    ? value 
                    : 0;
                Logger.Debug("Change round to {0} {1} ({2})", _currentRound, CurrentTimeUnit.GetType(), CurrentTimeUnit.Title);


                // reset counter
                Counter = 0;
                // reset alert
                Alert = null;

                if (CurrentTimeUnit is Break)
                {
                    startBreak();
                }
                if (CurrentTimeUnit is Pomodoro)
                {
                    startPomodoro();
                }
            }
        }
        int _currentRound = 0;

        /// <summary>
        /// Additional time 
        /// </summary>
        float IdleDeltaCounter
        {
            get
            {
                return _idleDeltaCounter;
            }
            set
            {
                // increase delta only for next pomodoro but not first
                if (CurrentRound <= 0)
                {
                    return;
                }

                _idleDeltaCounter = value;
                Logger.Trace("Set IdleDeltaCounter = {0}", _idleDeltaCounter);

                // if idle counter > the previous break time then set previous pomodoro
                if (_idleDeltaCounter > getPrevTime(typeof(Break)).CounterLimit)
                {
                    CurrentRound = getPrevTimeIndex(typeof(Pomodoro));
                    _idleDeltaCounter = 0; // reset
                }
            }
        }
        float _idleDeltaCounter;


        /// <summary>
        /// Visible property sometimes true inside Timer.Tick event when actually it's false
        /// so use own robust property
        /// </summary>
        public bool Visibility = true;


        /// <summary>
        /// How many extra time added for a next break (seconds)
        /// </summary>
        int ExtraBreakTime = 0;

        /// <summary>
        /// Counter of user requested extra breaks for current pomodoro
        /// </summary>
        int ExtraCounter = 0;



        public TomatoForm()
        {
            InitializeComponent();

            var configFile = System.IO.Path.Combine(Application.StartupPath, "config.hjson");
            try
            {
                config = new Config(configFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error loading " + configFile);
                Environment.Exit(1);
            }


            this.Width = this.Height = 0;
            this.Hide();

            // fill tray context
            var menuQuit = new MenuItem()
            {
                Text = config.LockMode 
                    ? config.GetPhrase("quit") + " " + config.GetPhrase("locked") 
                    : config.GetPhrase("quit"),
                Enabled = !config.LockMode
            };
            menuQuit.Click += MenuQuit_Click;

            var menuAbout = new MenuItem() { Text = config.GetPhrase("about") };
            menuAbout.Click += MenuAbout_Click;

            var menuAutostart = new MenuItem()
            {
                Text = config.LockMode
                    ? config.GetPhrase("autostart") + " " + config.GetPhrase("locked")
                    : config.GetPhrase("autostart"),
                Name = "menuAutostart",
                Checked = WinApi.GetStartup(config.ApplicationName, Application.ExecutablePath),
                Enabled = !config.LockMode
            };
            menuAutostart.Click += MenuAutostart_Click;

            var menuStart = new MenuItem()
            {
                Text = config.GetPhrase("start"),
                Name = "menuStart",
                Enabled = false
            };
            menuStart.Click += MenuStart_Click;

            var menuStop = new MenuItem()
            {
                Text = config.LockMode
                    ? config.GetPhrase("stop") + " " + config.GetPhrase("locked")
                    : config.GetPhrase("stop"),
                Name = "menuStop",
                Enabled = !config.LockMode
            };
            menuStop.Click += MenuStop_Click;

            var menuAddTimer = new MenuItem() { Text = config.GetPhrase("addtimer") };
            menuAddTimer.Click += MenuAddTimer_Click;

            var contextMenu = new System.Windows.Forms.ContextMenu();
            contextMenu.MenuItems.AddRange(new MenuItem[]
            {
                menuAddTimer,
                new MenuItem() { Text = "-" },
                menuStart,
                menuStop,
                menuAutostart,
                new MenuItem() { Text = "-" },
                menuAbout,
                menuQuit
            });
            this.notifyIcon1.Text = this.Text = config.ApplicationName;
            this.notifyIcon1.ContextMenu = contextMenu;

            lblDownTitle.Text = config.LockMode
                ? config.GetPhrase("lockmodesubtitle")
                : config.GetPhrase("freemodesubtitle");

        }

        private void MenuStop_Click(object sender, EventArgs e)
        {
            notifyIcon1.ContextMenu.MenuItems.Find("menuStop", false).First().Enabled = false;
            notifyIcon1.ContextMenu.MenuItems.Find("menuStart", false).First().Enabled = true;

            Logger.Info("Stopped by user");
            timeUnitTimer.Enabled = false;
            CurrentRound = 0;
            Counter = CurrentTimeUnit.CounterLimit; // set visible counter = 0:00
            updateTomatoPosition();
            updateElementsPosition();
            this.FadeOut();
        }

        private void MenuStart_Click(object sender, EventArgs e)
        {
            notifyIcon1.ContextMenu.MenuItems.Find("menuStop", false).First().Enabled = true;
            notifyIcon1.ContextMenu.MenuItems.Find("menuStart", false).First().Enabled = false;

            Logger.Info("Started by user");
            resetExtraMode();
            ExtraBreakTime = ExtraCounter = 0;

            CurrentRound = 0; // reset counter
            timeUnitTimer.Enabled = true;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Logger.Info("Initialization...");

            timeUnitTimer = new Timer()
            {
                Interval = 1000,
                Enabled = true
            };
            tomatoShowTimer = new Timer()
            {
                Interval = 10,
                Enabled = true
            };

            timeUnitTimer.Tick += _timeUnitTimer_Elapsed;
            tomatoShowTimer.Tick += TomatoShowTimer_Tick;

            lblBreakTime.Font = new Font(FontFamily.GenericSerif, Helper.GetBreakTimerFontSize());
            lblBreakTime.ForeColor = config.Face.BreakForeground;

            lblPomodoroTime.Font = new Font(FontFamily.GenericSerif, Helper.GetTimerFontSize());
            lblPomodoroTime.ForeColor = config.Face.PomodoroForeground;

            this.WindowState = FormWindowState.Normal;


            // load state from registry
            {
                lastActiveTime = config.LoadLastActiveTimeState();
                // if last active time was too late then reset a timer, because otherwise we can see a big lag while time adjusting
                if ((DateTime.Now - lastActiveTime).TotalSeconds > config.Times.Sum(t => t.CounterLimit))
                {
                    CurrentRound = 0;
                }
                else
                {
                    CurrentRound = config.LoadCurrentRoundState();
                    Counter = config.LoadCounterState();
                }
            }


            Logger.Info("Initialized");
        }

        private void MenuAutostart_Click(object sender, EventArgs e)
        {
            // toggle startup
            try
            {
                var check = false;
                if (WinApi.GetStartup(config.ApplicationName, Application.ExecutablePath))
                {
                    WinApi.SetStartup(config.ApplicationName, Application.ExecutablePath, false);
                    Logger.Debug("Unet startup");
                }
                else
                {
                    WinApi.SetStartup(config.ApplicationName, Application.ExecutablePath, true);
                    check = true;
                    Logger.Debug("Set startup");
                }
                notifyIcon1.ContextMenu.MenuItems.Find("menuAutostart", false).First().Checked = check;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MenuAbout_Click(object sender, EventArgs e)
        {
            var content = new System.Text.StringBuilder();
            content.AppendLine(string.Format("{0} v{1}", config.ApplicationName, config.Version));
            content.AppendLine(config.ApplicationDescription);
            content.AppendLine(config.ApplicationCopyright);
            content.AppendLine("----------------------------------------------------");
            content.AppendLine("License: https://www.gnu.org/licenses/gpl.html");
            content.AppendLine();
            content.AppendLine("Do you want to open the project home page?");
            content.AppendLine(" " + config.ApplicationHomePage);

            var result = MessageBox.Show(content.ToString(), "About " + config.ApplicationName, MessageBoxButtons.OKCancel, MessageBoxIcon.None);
            if (result == DialogResult.OK)
            {
                System.Diagnostics.Process.Start(config.ApplicationHomePage);
            }
        }


        private void MenuQuit_Click(object sender, EventArgs e)
        {
            this.Close();
            Environment.Exit(0);
        }
        private void MenuAddTimer_Click(object sender, EventArgs e)
        {
            if (customTimers.Count >= 7)
            {
                MessageBox.Show(config.GetPhrase("maxtimerswarn"), config.GetPhrase("maxtimerswarntitle"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            var frm = new NewUserTimerForm(config);
            frm.ShowDialog(this);
        }
        private void MenuRemoveTimer_Click(object sender, EventArgs e)
        {
            var item = (MenuItem)sender;
            RemoveCustomTimer(item.Name);
        }

        public bool IsIdle
        {
            get
            {
                // idle always false if "stop mode"
                if (!timeUnitTimer.Enabled)
                {
                    return false;
                }
                // if zero then disable idle mode
                if (config.IdleTime == 0)
                {
                    return false;
                }
                // handle idle only for pomodoro
                if (!IsPomodoro)
                {
                    return false;
                }


                // update idle time
                var idleTime = WinApi.IdleTimeFinder.GetIdleTimeSec();

                var idle = idleTime >= config.IdleTime;
                if (!_idle && idle)
                {
                    if (CurrentTimeUnit.ExtraMode)
                    {
                        Logger.Info("Force next round due to idle in extramode");
                        Counter = CurrentTimeUnit.CounterLimit;
                        return false;
                    }

                    _idle = true;
                    Logger.Info("Start idle");

                    // change interval for timer when idle
                    int newInterval = (int)(decimal.Divide(getPrevTime(typeof(Break)).CounterLimit, CurrentTimeUnit.CounterLimit) * 1000);
                    timeUnitTimer.Interval = newInterval;
                    Logger.Debug("Set timer interval = {0}", timeUnitTimer.Interval);
                    updateElementsPosition();
                }
                if (_idle && !idle)
                {
                    _idle = false;
                    Logger.Info("End idle");
                    IsAfterBreak = false;

                    // reset timer interval
                    timeUnitTimer.Interval = 1000;
                    Logger.Debug("Set timer interval = {0}", timeUnitTimer.Interval);
                    updateElementsPosition();
                }
                return idle;
            }
        }
        private bool _idle = false;

        /// <summary>
        /// First time after a break
        /// </summary>
        bool IsAfterBreak
        {
            get
            {
                // if stopped mode then it's not after a break
                if (!timeUnitTimer.Enabled)
                {
                    return false;
                }

                if (_isAfterBreak)
                {
                    if (Counter < config.WorkTimerShowFirstTime)
                    {
                        return true;
                    }
                }
                return false;
            }
            set
            {
                _isAfterBreak = value;
            }
        }
        bool _isAfterBreak = true;


        public bool IsPomodoro
        {
            get
            {
                return CurrentTimeUnit is Pomodoro;
            }
        }

        /// <summary>
        /// Current time unit (pomodoro or break)
        /// </summary>
        public TimeUnit CurrentTimeUnit
        {
            get
            {
                if (CurrentRound > config.Times.Count-1 || CurrentRound < 0)
                {
                    Logger.Error("Bad current round {0}", CurrentRound);
                    CurrentRound = 0;
                }
                return config.Times[CurrentRound];
            }
        }


        /// <summary>
        /// Timer to handle mouse move
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TomatoShowTimer_Tick(object sender, EventArgs e)
        {
            if (IsPomodoro)
            {
                // if mouse cursor in hot area
                // or idle
                // or first 5 seconds of pomodoro
                if (mouseShowPomodoro() || IsIdle || IsAfterBreak)
                {
                    if (AllowMouseEventForCurrentProcess())
                    {
                        // show form
                        this.FadeIn(true);
                    }
                }
                else
                {
                    if (Alert == null)
                    {
                        this.FadeOut();
                    }
                }
            }
        }

        /// <summary>
        /// Main timer loop
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _timeUnitTimer_Elapsed(object sender, EventArgs e)
        {
            Logger.Trace("Main timer tick");
            handleSleepLeap();

            // check for round finish
            if (Counter >= CurrentTimeUnit.CounterLimit + ((CurrentTimeUnit is Break) ? ExtraBreakTime : 0))
            {
                // increase round (CurrentTimeUnit automatically increases)
                CurrentRound++;

                if (CurrentTimeUnit is Break)
                {
                    var prevTime = getPrevTime(typeof(Pomodoro));
                    if (prevTime.ExtraMode)
                    {
                        resetExtraMode();
                        // add break time
                        ExtraBreakTime += getExtraBreakTime();
                        Logger.Trace("Set breaktime = {0}", ExtraBreakTime);
                    }
                }
                // Pomodoro
                else
                {
                    // reset if was set
                    ExtraBreakTime = 0;
                    Logger.Trace("Reset breaktime");
                    ExtraCounter = 0;

                    // also reset idle
                    IdleDeltaCounter = 0;
                }

                return;
            }

            // handle idle
            if (IsPomodoro)
            {
                // pause pomodoro if idle
                if (IsIdle)
                {
                    if (Counter > 0)
                    {
                        Counter--;
                        // update pomodoro timer
                        goto updatePositions;
                    }
                    else
                    {
                        IdleDeltaCounter += (float)timeUnitTimer.Interval / 1000;
                    }
                    // do nothing if counter <= 0
                    return;
                }
            }

            Counter++;

            if (IsPomodoro)
            {
                // handle alerts
                foreach (var alert in config.Alerts)
                {
                    if (alert.Remain == CurrentTimeUnit.CounterLimit - Counter)
                    {
                        Helper.PlaySound(alert.Sound);
                        // show alert only for all apps except silence 
                        if (AllowMouseEventForCurrentProcess())
                        {
                            // do not show custom timers when alert
                            this.FadeIn(true, false);
                        }

                        alert.Reset();
                        Alert = alert;
                    }
                }
                if (Alert != null)
                {
                    Alert.Elapsed++;
                    if (Alert.Elapsed > Alert.Duration)
                    {
                        Alert = null;
                    }
                }
            }

            updatePositions:

            // set label position
            if (CurrentTimeUnit is Break)
            {
                updateBreakPosition();
            }
            if (CurrentTimeUnit is Pomodoro)
            {
                updateTomatoPosition();
            }
            updateElementsPosition();

            config.SaveCurrentState(Counter, CurrentRound, lastActiveTime);
        }

        private void resetExtraMode()
        {
            for (var i = 0; i < config.Times.Count; i++)
            {
                config.Times[i].ExtraMode = false;
            }
        }

        /// <summary>
        /// Last timer active time
        /// </summary>
        DateTime lastActiveTime;
        private void handleSleepLeap()
        {
            var now = DateTime.Now;

            var diff = (int)(now - (DateTime)lastActiveTime).TotalSeconds;

            // if was leap
            if (diff > config.IdleTime)
            {
                Logger.Info("Time leap detected for {0} seconds", diff);
                // iterate diff untill null
                while (diff > 0)
                {
                    diff--;
                    if (Counter > 0)
                    {
                        Counter--;
                    }
                    else
                    {
                        IdleDeltaCounter++;
                    }
                }

                // also reset extramode
                resetExtraMode();
            }
            // set to current date
            lastActiveTime = now;
        }

        private void startBreak()
        {
            if (config.LockMode)
            {
                Logger.Debug("Lock keyboard");
                WinApi.InterceptKeys.LockKeyboard();
            }

            btnExtraTime.Visible = ExtraCounter < config.MaxExtraTimes; // show extra button only if allowed
            this.toolTip1.SetToolTip(this.btnExtraTime,
                string.Format(config.GetPhrase("addextratime"),
                    config.ExtraTime / 60,
                    getExtraBreakTime() / 60
                )
            );

            this.FadeOut(true);

            Logger.Info("Start break[{0}] ({1})", CurrentRound, CurrentTimeUnit.Title);
            Helper.PlaySound(config.BreakSound);

            // update form size equal to screen size
            this.Top = this.Left = 0;
            this.BackColor = config.Face.BreakBackground;

            lblBreakTime.Show();
            lblPomodoroTime.Hide();

            updateBreakPosition();
            updateElementsPosition();

            this.FadeIn(false);

            // focus form
            this.Activate();
            this.Focus();
        }
        private void updateBreakPosition()
        {
#if !DEBUG
            // update size to make sure all the screen always filled
            this.Width = Screen.PrimaryScreen.Bounds.Width;
            this.Height = Screen.PrimaryScreen.Bounds.Height;
#endif
            lblBreakTime.Text = Helper.GetTimeElapsedString(Counter, CurrentTimeUnit.CounterLimit + ExtraBreakTime);

            lblBreakTime.Left = this.Width / 2 - lblBreakTime.Width / 2;
            lblBreakTime.Top = this.Height / 2 - lblBreakTime.Height / 2;
        }

        private void startPomodoro()
        {
            if (config.LockMode)
            {
                Logger.Debug("Unlock keyboard");
                WinApi.InterceptKeys.UnlockKeyboard();
            }

            if (timeUnitTimer.Enabled)
            {
                Logger.Info("Start pomodoro[{0}|{1}] ({2})", CurrentRound, pomodoroCounter, CurrentTimeUnit.Title);
                Helper.PlaySound(config.PomodoroSound);

                pomodoroCounter++;
            }



            IsAfterBreak = true;
            btnExtraTime.Visible = false;
            this.FadeOut();

            var pos = getPomodoroPosition();
            this.Top = pos.X;
            this.Left = pos.Y;

            this.BackColor = config.Face.PomodoroBackground;

            lblPomodoroTime.Show();
            lblBreakTime.Hide();

            updateTomatoPosition();
            updateElementsPosition();
        }

        private void updateTomatoPosition()
        {
            // update form size to make sure it always correct
            var size = Helper.GetFormSize();
            this.Width = size.X;
            this.Height = size.Y;

            lblPomodoroTime.Text = Helper.GetTimeElapsedString(Counter, CurrentTimeUnit.CounterLimit);

            lblPomodoroTime.Left = this.Width / 2 - lblPomodoroTime.Width / 2;
            lblPomodoroTime.Top = this.Height / 2 - lblPomodoroTime.Height / 2;
        }

        private void updateElementsPosition()
        {
            // set always on top 
            if (!this.TopMost)
            {
                this.TopMost = true;
            }

            // title
            var fontSizeTitle = IsPomodoro ? Helper.GetTitleFontSize() : Helper.GetBreakTitleFontSize();
            lblTitle.Font = lblDownTitle.Font = new Font(FontFamily.GenericSerif, fontSizeTitle);

            lblTitle.Text = IsIdle
                ? config.GetPhrase("idletitle")
                : CurrentTimeUnit.ExtraMode
                    ? config.GetPhrase("extramodetitle")
                    : timeUnitTimer.Enabled
                        ? CurrentTimeUnit.Title
                        : config.GetPhrase("stopmodetitle");
            lblTitle.Left = this.Width / 2 - lblTitle.Width / 2;
            lblTitle.Top = (IsPomodoro ? lblPomodoroTime.Top : lblBreakTime.Top) / 4;
            lblTitle.ForeColor = lblDownTitle.ForeColor = IsPomodoro ? config.Face.PomodoroForeground : config.Face.BreakForeground;

            if (!IsPomodoro)
            {
                lblDownTitle.Show();
                lblDownTitle.Left = this.Width / 2 - lblDownTitle.Width / 2;
                lblDownTitle.Top = this.Height - (this.Height - lblBreakTime.Top) / 4;

                btnExtraTime.Width = btnExtraTime.Height = this.Height / 12;
                btnExtraTime.Left = this.Height - lblDownTitle.Top - btnExtraTime.Height;
                btnExtraTime.Top = lblDownTitle.Top;
            }
            else
            {
                lblDownTitle.Hide();
            }
        }





        private Point getPomodoroPosition()
        {
            var sw = Screen.PrimaryScreen.Bounds.Width;
            var sh = Screen.PrimaryScreen.Bounds.Height;
            var fw = this.Width;
            var fh = this.Height;

            // (top, left)
            Point point;
            switch (config.Position)
            {
                case "top-right":
                    point = new Point(0, sw - fw);
                    break;
                case "top-left":
                    point = new Point(0, 0);
                    break;
                case "bottom-left":
                    point = new Point(sh - fh, 0);
                    break;
                case "bottom-right":
                    point = new Point(sh - fh, sw - fw);
                    break;
                case "bottom":
                    point = new Point(sh - fh, (sw / 2) - (fw / 2));
                    break;
                case "top":
                    point = new Point(0, (sw / 2) - (fw / 2));
                    break;
                case "left":
                    point = new Point((sh / 2) - (fh / 2), 0);
                    break;
                case "right":
                    point = new Point((sh / 2) - (fh / 2), sw - fw);
                    break;
                default:
                    goto case "top-left";
            }
            return point;
        }

        private bool mouseShowPomodoro()
        {
            var area = config.MouseArea;
            var w = Screen.PrimaryScreen.Bounds.Width;
            var h = Screen.PrimaryScreen.Bounds.Height;
            var x = Cursor.Position.X;
            var y = Cursor.Position.Y;

            switch (config.Position)
            {
                case "top-right":
                    return x > w - area && y < area;
                case "top-left":
                    return x < area && y < area;
                case "bottom-left":
                    return x < area && y > h - area;
                case "bottom-right":
                    return x > w - area && y > h - area;
                case "bottom":
                    return y > h - area;
                case "top":
                    return y < area;
                case "left":
                    return x < area;
                case "right":
                    return x > w - area;
                default:
                    goto case "top-left";
            }
        }


#region show/hide animation

        /// <summary>
        /// Show form with fade animation
        /// </summary>
        /// <param name="immediate">without animation</param>
        public void FadeIn(bool immediate = false, bool customTimers = true)
        {
            if (customTimers)
            {
                toggleUserTimers(true);
            }

            // do nothing if already visible
            if (this.Visibility)
            {
                return;
            }
            this.Opacity = 0;
            this.Left = this.Top = 0; //instead this.Show(); which toggle focus
            Logger.Trace("Show");


            this.Visibility = true;
            if (immediate)
            {
                this.Opacity = 100;
                return;
            }
            // fade in up to 95% (for half-visible break window)
            for (int i = 1; i < 95; i += 3)
            {
                System.Threading.Thread.Sleep(1);
                var opacity = (double)i / 100;
                this.Opacity = opacity;
                this.Refresh();
            }

        }

        /// <summary>
        /// Hide form with fade animation
        /// </summary>
        /// <param name="immediate">without animation</param>
        public void FadeOut(bool immediate = false)
        {
            // do nothing if already hidden
            if (!this.Visibility)
            {
                return;
            }
            Logger.Trace("Hide");
            toggleUserTimers(false);
            // if customtimers not null then also immediate hide
            if (immediate || customTimers.Count > 0)
            {
                goto complete;
            }
            for (int i = 100; i > 0; i -= 3)
            {
                System.Threading.Thread.Sleep(1);
                var opacity = (double)i / 100;
                this.Opacity = opacity;
                this.Refresh();
            }
            complete:

            this.Left = -this.Width; //instead this.Hide(); which toggle focus
            this.Top = -this.Height;
            this.Visibility = false;
        }



#endregion


        /// <summary>
        /// Return previous time index from current (break or pomodoro)
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private int getPrevTimeIndex(Type type)
        {
            var i = CurrentRound;
            while (true)
            {
                i--;
                if (i < 0)
                {
                    i = config.Times.Count - 1;
                }
                if (config.Times[i].GetType() == type)
                {
                    return i;
                }
            }
        }
        private TimeUnit getPrevTime(Type type)
        {
            var i = getPrevTimeIndex(type);
            return config.Times[i];
        }


        /// <summary>
        /// Check if current active app is in silence apps
        /// </summary>
        /// <returns></returns>
        private bool AllowMouseEventForCurrentProcess()
        {
            try
            {
                var pname = WinApi.GetActiveProcessFileName();
                foreach (var app in config.SilenceApps)
                {
                    if (pname.ToLower() == app.ToLower())
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Return how many seconds add to extra break
        /// </summary>
        /// <returns></returns>
        private int getExtraBreakTime()
        {
            var a = (double)CurrentTimeUnit.CounterLimit;
            var b = (double)getPrevTime(typeof(Pomodoro)).CounterLimitOriginal / config.ExtraTime;
            int result = (int)Math.Floor(a / b);
            return result;
        }


#region User Timers


        /// <summary>
        /// 
        /// </summary>
        /// <param name="show">show or hide</param>
        private void toggleUserTimers(bool show)
        {
            // show only if not alert
            if (show)
            {
                updateUserTimerIDs();
                customTimers.OrderBy(t => t.Value.Seconds).ToList().ForEach(t => t.Value.Show());
            }
            else
            {
                customTimers.OrderBy(t => t.Value.Seconds).ToList().ForEach(t => t.Value.Hide());
            }
        }

        private void updateUserTimerIDs()
        {
            var i = 0;
            // update timers ID to update positioon when old timers deleted
            customTimers.OrderBy(t => t.Value.Seconds).ToList().ForEach(t => { t.Value.ID = ++i; });
        }


        /// <summary>
        /// Add custom timer into 
        /// </summary>
        /// <param name="title"></param>
        /// <param name="seconds">when timer should be elapsed</param>
        /// <returns></returns>
        public string AddCustomTimer(string title, int seconds)
        {
            var key = Guid.NewGuid().ToString();

            MenuItem menuRemoveTimer;
            // add "Remove Timer" menu item if not exist
            var find = notifyIcon1.ContextMenu.MenuItems.Find("menuRemoveItem", false);
            if (find.Count() == 0)
            {
                menuRemoveTimer = new MenuItem() { Text = config.GetPhrase("removetimer"), Name = "menuRemoveItem" };
                notifyIcon1.ContextMenu.MenuItems.Add(0, menuRemoveTimer);
            }
            else
            {
                menuRemoveTimer = find.First();
            }
            // add timer menu item 
            var menuTimerItem = new MenuItem()
            {
                Text = !string.IsNullOrEmpty(title) ? title : config.GetPhrase("timerempty"),
                Name = key
            };
            menuTimerItem.Click += MenuRemoveTimer_Click;
            menuRemoveTimer.MenuItems.Add(menuTimerItem);

            // add timer
            var utimer = new UserTimer(key, title, seconds, config, this);
            utimer.StoppedEvent += Utimer_StoppedEvent;
            customTimers.Add(key, utimer);

            // show first time
            updateUserTimerIDs();
            utimer.Show();

            return key;

        }

        /// <summary>
        /// When user timer stopped
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Utimer_StoppedEvent(object sender, EventArgs e)
        {
            var utimer = (UserTimer)sender;
            RemoveCustomTimer(utimer.Key);
        }

        private void btnExtraTime_Click(object sender, EventArgs e)
        {
            ExtraCounter++;
            getPrevTime(typeof(Pomodoro)).ExtraMode = true;

            // return to a pomodoro and set extramode
            CurrentRound--;

            updateTomatoPosition();
            updateElementsPosition();
        }

        private void btnExtraTime_Click_1(object sender, EventArgs e)
        {
            // INFO: do nothing to avoid add extratime by "enter" or "space"
            //       (click is implemented only by mousedown)
        }

        public void RemoveCustomTimer(string key)
        {
            notifyIcon1.ContextMenu.MenuItems.RemoveByKey(key);

            var find = notifyIcon1.ContextMenu.MenuItems.Find("menuRemoveItem", false);
            if (find.Count() > 0)
            {
                var menuItem = find.First();
                // remove item
                menuItem.MenuItems.RemoveByKey(key);

                // if there are no more timers then remove menu "Remove Timer"
                if (menuItem.MenuItems.Count == 0)
                {
                    notifyIcon1.ContextMenu.MenuItems.Remove(menuItem);
                }
            }

            // remove timer
            customTimers[key].Destroy();
            customTimers.Remove(key);
        }


#endregion
    }
}
