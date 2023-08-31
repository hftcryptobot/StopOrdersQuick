using DevExpress.Data.Utils;
using QuikSharp;
using QuikSharp.DataStructures;
using StockSharp.Algo.Indicators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace QuikTester
{
    public class QuikConnector : Logger
    {
        private bool GetCandleHistoryOnce = true;
        bool firsttimeLoadForPositions = false;
        private Quik _quikconnector;
        /// <summary>
        /// строчка из классов нужня для метода по соответствию кода и класса
        /// адаптировал
        /// </summary>
        private string stringclasses = "";

        /// <summary>
        /// Содержит все активные стоп заявки. При загрузке истории находит стопы с комментарием "bot"
        /// </summary>
        public ConcurrentDictionary<long, StopOrder> ActiveStopOrders = new ConcurrentDictionary<long, StopOrder>();
        
        ConcurrentDictionary<string, decimal> ActivePositions = new ConcurrentDictionary<string, decimal>();
        /// <summary>
        /// Словарь который хранит значения для передвижения скользщек
        /// ключ - инструмент
        /// значение значение скользящей...
        /// </summary>
        ConcurrentDictionary<string, decimal> BuyEmaValues = new ConcurrentDictionary<string, decimal>();

        public Action Disconnected;


        public void Connect()
        {

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _quikconnector = new Quik(34130, new InMemoryStorage(), "127.0.0.1");

            _quikconnector.StopOrders.NewStopOrder += StopOrders_NewStopOrder;

            if (_quikconnector.IsServiceConnected())
            {
                LogMessage("Подключились ");


                //все что нужно произвести при первом подключении
                //0. 
                GetAllClassCodesForInstruments();


                //1. дергаем стопы в базу нашу локальную 
                GetStopOrders();

                //2. включаем таймер который берет позиции по всем инструментам 
                var timer = new Timer(1000);
                timer.Elapsed += (s, e) =>
                {
                    if (!inprocess)
                        GetPositions();
                };
                timer.Start();



            }
        }



        bool inprocess = false;

        /// <summary>
        /// Прилетает каждые N секунд 
        /// Работает по таймеру
        /// </summary>
        public Action<ConcurrentDictionary<string, decimal>> PositionsUpdate;

        private void GetPositions()
        {
            inprocess = true;

            //все позы со всех мест (акции, фьючи и так далее)
            //у всех свой формат и так далее
            //поэтому этот словарь просто агрегирует инструмент и текущую позу... все
            var allposes = new ConcurrentDictionary<string, decimal>();

            //отправляет не все позиции (фьючерсове нет) берем T2
            var stockPositions = _quikconnector.Trading.GetDepoLimits().Result.Where(s => s.LimitKindInt == 2);
            foreach (var pos in stockPositions) { allposes.TryAdd(pos.SecCode, pos.CurrentBalance); }

            //фьючерсы 
            var futPositions = _quikconnector.Trading.GetFuturesClientHoldings().Result;
            foreach (var pos in futPositions) { allposes.TryAdd(pos.secCode, (decimal)pos.totalNet); }

            foreach (var pos in allposes)
            {
                var sec = pos.Key;
                var currentpos = pos.Value;

                //Debug.WriteLine($"{sec} Позиция {currentpos}");

                //LogMessage($"Открыта позиция {currentpos} {sec}");
                if (firsttimeLoadForPositions)
                {
                    ActivePositions.TryAdd(sec, currentpos);
                }
                else
                {
                    decimal previouspos = 0;

                    if (ActivePositions.ContainsKey(sec))
                    {
                        previouspos = ActivePositions[sec];

                    }
                    else
                        ActivePositions.TryAdd(sec, currentpos);

                    if (currentpos != 0 && currentpos != previouspos)
                    {
                        LogMessage($"Зафиксировано изменение позиции {sec} прошлая поза = {previouspos} новая поза = {currentpos}");
                        ActivePositions[sec] = currentpos;
                    }

                    if (currentpos == 0 && currentpos != previouspos)
                    {
                        LogMessage($"Произошло  {sec} закрытие позциии = {previouspos} новая поза = {currentpos}");
                    }

                }
            }


            PositionsUpdate?.Invoke(ActivePositions);


            Debug.WriteLine($"-------------------");


            GetCandelHistoryForActivePositions();


            Debug.WriteLine($"-------------------");

            firsttimeLoadForPositions = false;
            inprocess = false;
        }

        private void StopOrders_NewStopOrder(StopOrder stop)
        {
            LogMessage($"{stop.SecCode} обновление стоп {stop.ConditionPrice} направление {stop.Operation} номер {stop.OrderNum} {stop.State}");
            UpdateStopOrderState(stop);
        }
        public string Account { get; set; }

        public void PlaceStopOrder(decimal price, int quantity, Operation direction, string seccode, int roundNumbers)
        {
            /*    
            {
               ConditionPrice = 6650,
                Price= 6650,
                SecCode = _securityCode,
                Quantity =1,
                StopOrderType = StopOrderType.StopLimit,
                Operation = Operation.Sell,
               
            };*/

            decimal marketslippage = 0.1m;
            var marketprice = Math.Round(direction == Operation.Buy ? price * (1+ marketslippage) : price * (1- marketslippage), roundNumbers);

            var stopOrder = new StopOrder()
            {
                ConditionPrice = price,
                Price = marketprice,
                SecCode= seccode,
                Quantity = quantity,
                StopOrderType = StopOrderType.StopLimit,
                Operation = direction,
            };

            stopOrder.Account = Account;
            stopOrder.ClassCode = GetClassCodeForInsturment(stopOrder.SecCode);


            _quikconnector.StopOrders.CreateStopOrder(stopOrder);
        }

        public void CancelStopOrder(StopOrder stopOrder)
        {
            _quikconnector.StopOrders.KillStopOrder(stopOrder);
        }
    


        private void UpdateStopOrderState(StopOrder stop)
        {

            if (stop.State == State.Active)
            {
                if (!ActiveStopOrders.ContainsKey(stop.OrderNum))
                {
                    LogMessage($"{stop.SecCode} Добавление нового стоп ордера {stop.ConditionPrice} направление {stop.Operation} номер {stop.OrderNum} {stop.State}");
                    ActiveStopOrders.TryAdd(stop.OrderNum, stop);
                }
                //OrderNum 
            }

            if (stop.State == State.Completed || stop.State == State.Canceled)
            {

                if (ActiveStopOrders.Remove(stop.OrderNum, out var deletedStopOrder))
                {
                    LogMessage($"{stop.SecCode} Стоп удален {stop.ConditionPrice} направление {stop.Operation} номер {stop.OrderNum} {stop.State}");
                }
            }
        }

        private void GetCandelHistoryForActivePositions()
        {
            //pos.key = код инструмента
            foreach (var pos in ActivePositions)
            {
                if (pos.Value != 0)
                {
                    var candles = _quikconnector.Candles.GetAllCandles(GetClassCodeForInsturment(pos.Key), pos.Key, CandleInterval.M1).Result;

                    //каждый раз создаем новую скользяшку и считаем 
                    var signalEma = new ExponentialMovingAverage() { Length = 10 };

                    //постоянно без последней свечки, потому что она всегда будет не законченной
                    for (int i = 0; i < candles.Count - 1; i++)
                    {
                        signalEma.Process(candles[i].Close, true);
                        //Debug.WriteLine("история " + candle.Datetime.day + "|" + candle.Datetime.hour + ":" + candle.Datetime.min + " " + (double)candle.Close);
                    }

                    //навправление покупки
                    if (pos.Value > 0)
                    {
                        //в первый раз добавляем 
                        if (BuyEmaValues.ContainsKey(pos.Key))
                        {

                        }
                    }


                    Debug.WriteLine($"скользящая {signalEma.GetValue(0)} Получено {pos.Key} свечек {candles.Count} . Последняя свечка {candles.Last().Close} время {((DateTime)candles.Last().Datetime)} ");
                }
            }

        }

        /// <summary>
        /// Метод отдает либо последнюю свечку (что является последней ценой)
        /// либо считает EMA 
        /// Для последней цены беру дневки (чтобы не было много данных, так как нам нужна только последняя цена)
        /// </summary>
        /// <param name="candleInterval"></param>
        /// <param name="_length"></param>
        /// <param name="posbot"></param>
        /// <param name="ema"></param>
        /// <returns></returns>
        public async Task<decimal> GetEmaValueOrLastPrice(PositionBot posbot,bool ema =true, CandleInterval candleInterval = CandleInterval.D1, int _length = 0)
        {
            

            var candles = await _quikconnector.Candles.GetAllCandles(GetClassCodeForInsturment(posbot.Symbol), posbot.Symbol, candleInterval);

            if (ema)
            {
                //каждый раз создаем новую скользяшку и считаем 
                var signalEma = new ExponentialMovingAverage() { Length = _length };

                //постоянно без последней свечки, потому что она всегда будет не законченной
                for (int i = 0; i < candles.Count - 1; i++)
                {
                    signalEma.Process(candles[i].Close, true);
                    //Debug.WriteLine("история " + candle.Datetime.day + "|" + candle.Datetime.hour + ":" + candle.Datetime.min + " " + (double)candle.Close);
                }


                return signalEma.GetValue(0);
            }

            return candles.LastOrDefault().Close;

        }

        /// <summary>
        /// Изначальный метод старта
        /// Получить позиции
        /// Получить стопы
        /// Проверить как они сведены... 
        /// </summary>
        private void GetStopOrders()
        {
            var stoporders = _quikconnector.StopOrders.GetStopOrders().Result.Where(s => s.Comment.Contains("bot")) ;

            if (stoporders != null)
                foreach (var stop in stoporders)
                {
                    LogMessage($"{stop.SecCode} стоп история {stop.ConditionPrice} направление {stop.Operation} номер {stop.OrderNum} {stop.State}");
                    UpdateStopOrderState(stop);
                    //добавляем наши стопчики 

                }
        }


        /*
            private void SubscribeForCandles()
            {
                _quikconnector.Candles.NewCandle += candle =>
                {
                    Debug.WriteLine($"Свечка {_securityCode} "+ candle.Datetime.day + "|" + candle.Datetime.hour  + ":" + candle.Datetime.min + " " + candle.Close);
                    //logger.WriteLine(candle.Datetime.day + "|" + candle.Datetime.hour  + ":" + candle.Datetime.min + " " + candle.Close);
                };

                var subscribe = _quikconnector.Candles.Subscribe(_classcode, _securityCode, CandleInterval.M1);
                subscribe.Wait();

                if (_quikconnector.Candles.IsSubscribed(_classcode, _securityCode, CandleInterval.M1).Result)
                {
                    LogMessage("Свечки подписались");
                }
            }*/

            public void Stop()
        {
            if (_quikconnector != null)
            {
                _quikconnector.Events.OnDisconnectedFromQuik += () =>
                {
                    if (Disconnected != null)
                        Disconnected();
                };

                _quikconnector.StopService();
            }

        }



        #region Helpers

        /// <summary>
        /// найти количество цифр после запятой 
        /// для округления цены для скользяшки и выставления стопа
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public static int getDecimalCount(decimal val)
        {
            int i = 0;
            while (Math.Round(val, i) != val)
                i++;
            return i;
        }


        /// <summary>
        /// Получить все коды классов - нужно нам потом для заказа свечек
        /// код инструмента + класс инструментов
        /// </summary>
        private void GetAllClassCodesForInstruments()
        {


            var codeclasses = _quikconnector.Class.GetClassesList().Result;
            foreach (var _class in codeclasses)
                stringclasses += $",{_class}";

            stringclasses.Remove(0);
        }
        private string GetClassCodeForInsturment(string code)
        {
            return _quikconnector.Class.GetSecurityClass(stringclasses, code).Result;
        }

        #endregion




    }
}
