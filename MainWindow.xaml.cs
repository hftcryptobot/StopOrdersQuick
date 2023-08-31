using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using System.Windows;
using DevExpress.Xpf.Editors.Themes;
using Ecng.ComponentModel;
using QuikSharp;
using QuikSharp.DataStructures;
using StockSharp.Algo.Candles;
using StockSharp.Algo.Indicators;


//todo сделать динамическое изменени колонок в зависимости от типа стратегии

namespace QuikTester
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : DevExpress.Xpf.Core.ThemedWindow
    {


        ObservableCollection<PositionBot> positionBots = new ObservableCollection<PositionBot>();

        // private ExponentialMovingAverage signallma;
        private string _classcode = "TQBR";
        private string _securityCode = "LKOH";


        private object objectlogger = new object();
        string prevmessage = "";
        private StreamWriter logger = new StreamWriter(DateTime.Now.ToString("dd_MM_yyyy") + ".txt", true);
        QuikConnector QuikConnector { get; set; }


        private void SetUISettings()
        {
            StrategyTypComboBoxEditSettings.ItemsSource = Enum.GetNames(typeof(StrategyType));
            CandlesTypesComboBoxEditSettings.ItemsSource = Enum.GetNames(typeof(CandleInterval));
        }

        public MainWindow()
        {
            InitializeComponent();


            
            SetUISettings();

            QuikConnector = new QuikConnector()
            {
                Account = "L01+00000F00"
            };

            if (File.Exists(Helper.SettingsFile))
            {
                positionBots = Helper.ReadXml<ObservableCollection<PositionBot>>();
            }

            BotPositionsGrid.ItemsSource = positionBots;

            QuikConnector.PositionsUpdate += (positions) =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {

                    //создаем список ботов исходя из позиций.. 
                    foreach (var pos in positions)
                    {
                        var bot = positionBots.FirstOrDefault(p => p.Symbol == pos.Key);
                        if (bot == null)
                            positionBots.Add(new PositionBot(QuikConnector) { Symbol = pos.Key, Activated = false, CurrentPos = pos.Value });
                        else
                        {
                            if (bot.QuikConnector == null)
                                bot.QuikConnector = QuikConnector;

                            bot.UpdatePosition(pos.Value);
                        }
                    }

                    BotPositionsGrid.RefreshData();


                }));

                Helper.SaveXml(positionBots);
            };
            QuikConnector.LogAction += LogMessage;
            QuikConnector.Connect();

        }


        protected override void OnClosing(CancelEventArgs e)
        {
            LogMessage("Отключаемся от квика");
            if (QuikConnector == null)
            {
                base.OnClosing(e);
                return;
            }

            try
            {
                QuikConnector.Disconnected += () => 
                {
                    base.OnClosing(e);
                };
                QuikConnector.Stop();
            }
            catch (Exception ex)
            {
                base.OnClosing(e);
            }
           
        }

        /*
        /// <summary>
        /// История свечек.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Button_Click_2(object sender, RoutedEventArgs e)
        {

            var candles = _quikconnector.Candles.GetAllCandles(_classcode, _securityCode, CandleInterval.H1).Result;
            // var candles = _quikconnector.Candles.GetLastCandles("QJSIM","SBER",CandleInterval.M1,1).Result;
           
            foreach (var candle in candles)
            {
                LogMessage("история " + candle.Datetime.day + "|" + candle.Datetime.hour + ":" +
                           candle.Datetime.min + " " + (double) candle.Close);
            }

        }
        */
        
        public async void LogMessage(string message)
        {

            try
            {

                if (message == prevmessage) return;

                var dt = DateTime.Now;
                var datetime = dt.ToString("H:mm:ss.fff");
                var logmessage = datetime + " | " + message;
                prevmessage = message;

                //Debug.WriteLine(message);

                if (Application.Current != null)
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        LogTextBox.AppendText(logmessage + Environment.NewLine);
                        LogTextBox.ScrollToEnd();

                    }));

                lock (objectlogger)
                {
                    logger.WriteLine(logmessage);
                }

                // logger.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

        }

        private void StartCheck_Click(object sender, RoutedEventArgs e)
        {

        }

        private void GetCandles_Click(object sender, RoutedEventArgs e)
        {
            //var candles = _quikconnector.Candles.GetAllCandles(_classcode, _securityCode, CandleInterval.M1).Result;
            // Debug.WriteLine($"скользящая {signallma.GetValue(0)} Получено {_securityCode} свечек {candles.Count} . Последняя свечка {candles.Last().Close} время {((DateTime)candles.Last().Datetime)} ");

            /*
            STOPPRICE
                SECCODE
            PRICE
            Quantity
            StoporderType
            Stopoperation = sell*/

            /*
            var stoporder = new StopOrder()
            {
                ConditionPrice = 6650,
                Price= 6650,
                SecCode = _securityCode,
                Quantity =1,
                StopOrderType = StopOrderType.StopLimit,
                Operation = Operation.Sell,
            };

            QuikConnector.PlaceStopOrder(stoporder);*/

        }

        private void RefreshButton(object sender, RoutedEventArgs e)
        {
            var bot = (PositionBot)BotPositionsGrid.SelectedItem;

            if(bot!=null)
                bot.RefreshPosBot();
        }
    }
}
