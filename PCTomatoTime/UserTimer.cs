﻿using NLog;
using PCTomatoTime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PCTomatoTime
{
    class UserTimer
    {
        // Declare the delegate (if using non-generic pattern).
        public delegate void StoppedEventHandler(object sender, EventArgs e);

        // Declare the event.
        public event StoppedEventHandler StoppedEvent;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private Form form;
        private Label lblTitle, lblDownTitle, lblTime;
        private Timer timer;

        public string Key
        {
            get
            {
                return _key;
            }
            private set
            {
                _key = value;
            }
        }
        private string _key;


        public int Seconds
        {
            get
            {
                return _seconds;
            }
            private set{
                _seconds = value;
            }
        }
        private int _seconds;

        public int Counter
        {
            get
            {
                return _counter;
            }
            private set
            {
                _counter = value;
            }
        }
        private int _counter;


        public bool Elapsed
        {
            get
            {
                return Counter >= Seconds;
            }
        }


        private Config config;

        /// <summary>
        /// Identifier (starting from 1)
        /// It used to calculate form position on the screen
        /// </summary>
        public int ID;

        private bool visibility = false;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="seconds">When timer should be stopped</param>
        public UserTimer(string key, string title, int seconds, Config config)
        {
            this.config = config;
            this.Seconds = seconds;
            this.Key = key;

            // init form
            form = new Form()
            {
                FormBorderStyle = FormBorderStyle.None,
                Width = Screen.PrimaryScreen.Bounds.Width / 8,
                Height = Screen.PrimaryScreen.Bounds.Height / 8,
                Cursor = Cursors.Hand,
                TopMost = true,
                ShowInTaskbar = false
            };


            lblTitle = new Label()
            {
                Text = title,
                AutoSize = true
            };
            lblDownTitle = new Label()
            {
                AutoSize = true
            };
            lblTime = new Label()
            {
                AutoSize = true
            };

            form.Click += Form_Click;
            lblTitle.Click += Form_Click;
            lblDownTitle.Click += Form_Click;
            lblTime.Click += Form_Click;


            // set font size
            var fontSize = Screen.PrimaryScreen.Bounds.Width / 50;
            lblTime.Font = new Font(FontFamily.GenericSerif, fontSize);

            var fontSizeTitle = lblTime.Font.Size / 4;
            lblTitle.Font = lblDownTitle.Font = new Font(FontFamily.GenericSerif, fontSizeTitle);


            // set colors
            lblTime.ForeColor = lblTitle.ForeColor = lblDownTitle.ForeColor = config.Face.UserTimerForeground;
            // set random background color
            form.BackColor = config.Face.UserTimerBackground[Helper.GetRandom(0, config.Face.UserTimerBackground.Count - 1)];

            // add labels to the form
            form.Controls.AddRange(new Control[] {
                lblTitle, lblDownTitle, lblTime
            });


            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += Timer_Tick;

            updateElementsPosition();
            Start();
        }

        private void Form_Click(object sender, EventArgs e)
        {
            if (StoppedEvent != null)
            {
                StoppedEvent(this, null);
            }
            form.Close();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (Counter == config.UserTimerShowFirstTime)
            {
                Hide(); 
            }

            Counter++;
            updateElementsPosition();

            Logger.Trace("User timer counter {0}: ", Key, Counter);

            // when elapsed then stop timer and show form
            if (Elapsed)
            {
                Stop();

                lblDownTitle.Text = string.Format("Stopped at {0}:{1}:{2}", DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second);

                Helper.PlaySound(config.UserTimerSound);

                Show();
            }
        }

        private void updateElementsPosition()
        {
            // form
            if (!visibility)
            {
                return;
            }

            // timer
            lblTime.Text = Helper.GetTimeElapsedString(Seconds, Counter);
            lblTime.Left = form.Width / 2 - lblTime.Width / 2;
            lblTime.Top = form.Height / 2 - lblTime.Height / 2;

            // title
            lblTitle.Left = form.Width / 2 - lblTitle.Width / 2;
            lblTitle.Top = lblTime.Top / 4;
            lblDownTitle.Left = form.Width / 2 - lblDownTitle.Width / 2;
            lblDownTitle.Top = lblTime.Top + lblTime.Height + (lblTime.Top / 4);
        }


        public void Show()
        {
            if (!visibility)
            {
                // if first time not visible then show form
                if (!form.Visible)
                {
                    form.Show();
                }
                visibility = true;

                var pos = getFormPosition(ID, form.Height);
                form.Left = pos.X;
                form.Top = pos.Y;
                form.TopMost = true;

                updateElementsPosition();
                Logger.Trace("Show user timer " + lblTitle.Text);
            }
        }

        public void Hide()
        {
            if (visibility && !Elapsed)
            {
                visibility = false;

                // make form invisible
                form.Left = -form.Width;
                form.Top = -form.Height;

                Logger.Trace("Hide user timer " + lblTitle.Text);
            }
        }

        public void Start()
        {
            timer.Start();
            Logger.Info("User timer started: {0}", Key);
        }

        public void Stop()
        {
            timer.Stop();
            Logger.Info("User timer elapsed: {0}", Key);
        }

        public void Destroy()
        {
            Stop();
            form.Dispose();
            Logger.Debug("Destroy user timer " + lblTitle.Text);
        }




        private Point getFormPosition(int id, int height)
        {
            // TODO: handle each possible form position like in getPomodoroPosition()
            return new Point(0, id * height);
        }
    }
}
