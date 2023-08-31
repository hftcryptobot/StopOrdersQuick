using DevExpress.Data.Utils;
using QuikSharp.DataStructures;
using StockSharp.Algo.Indicators;
using StockSharp.BusinessEntities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuikTester
{
    [DataContract]
    public enum StrategyType
    {
        [EnumMember]
        Ema,
        [EnumMember]
        Percent,
        [EnumMember]
        ValueDiff
    }

    [DataContract]
    public  class PositionBot: Logger
    {
        private Operation prevDirection;

        /// <summary>
        /// высчитанный уровень в зависимости от направления 
        /// </summary>
        public  decimal PriceDeltaNow { get; set; }

        [DataMember]
        public string Symbol { get; set; }

        public decimal CurrentPos { get; set; }
        [DataMember]
        public bool Activated { get; set; }

        public QuikConnector QuikConnector { get; set; }

        [DataMember]
        public StrategyType StrategyType { get; set; }

        public bool Started { get; set; } = false;
        public string StrategyTypeString
        {
            get => StrategyType.ToString();
            set
            {
                Enum.TryParse<StrategyType>(value, out var result);
                StrategyType = result;
            }
        }

        /*------- три типа стратегии и их свойства-----*/

        [DataMember]
        public decimal LastEmaValue { get; set; }
        public decimal LastPrice { get; private set; }

        /// <summary>
        /// Дельта для высчитывания в пунтках
        /// </summary>
        [DataMember]
        public decimal Delta { get; set; }

        /// <summary>
        /// Процентное значение
        /// указывается в реальных процентах
        /// </summary>
        [DataMember]
        public decimal Percent { get; set; }

        /// <summary>
        /// Цена "отступа" в процентах или в реальном значении... зависит от настроек типа стратегии
        /// </summary>
        public decimal CurretPriceDelta { get;set;
           /* get
            {
                if (LastPrice == 0) return 0;
                if (Direction == Operation.Buy) return LastPrice - Delta;

                return LastPrice + Delta;
            }*/
        }



        /* -------------------------------------------*/


        [DataMember]
        public CandleInterval CandleInterval { get; set; }

        public string CandleIntervalString
        {
            get => CandleInterval.ToString();
            set
            {
                Enum.TryParse<CandleInterval>(value, out var result);
                CandleInterval = result;
            }
        }
        
        [DataMember]
        public int EmaLength { get; set; }


        /// <summary>
        /// По умолчанию ставлю на покупку направление
        /// todo потом сделаю динамчиеским 
        /// </summary>
        public Operation Direction { get; set; } = Operation.Buy;

        public PositionBot(QuikConnector quikConnector)
        {
            QuikConnector = quikConnector;
        }

        public async void UpdatePosition(decimal newPos)
        {
            if (newPos > 0) Direction = Operation.Buy;
            if (newPos < 0) Direction = Operation.Sell;


            if (Activated && Started)
            {

                //Произошло закрытие позиции...
                if (newPos == 0 && newPos != CurrentPos)
                {
                    LogMessage("Позиция обнулилась. Отменяем стоп ордер ");
                    TryToFindAndCancelStopOrder();
                }

                /*----------- решил сделать калькуляцию параметров вне зависимости от того запущена или нет ----- */ 


                /*--------------------------------------------------------------------------------------------------*/

                if (newPos != 0)
                {

                    if (StrategyType == StrategyType.Percent || StrategyType == StrategyType.ValueDiff)
                    {
                        LastPrice = await QuikConnector.GetEmaValueOrLastPrice(posbot: this, ema: false);

                        if (StrategyType == StrategyType.ValueDiff)
                            PriceDeltaNow = Direction == Operation.Buy ? LastPrice - Delta : LastPrice + Delta;
                        else
                            PriceDeltaNow = Direction == Operation.Buy
                                ? LastPrice * (1 - Percent / 100)
                                : (1 + Percent / 100);

                        //сменилось направление или с самого нуля стартуем
                        if ((CurretPriceDelta == 0 /*& newPos != 0*/) || prevDirection != Direction)
                        {
                            LogMessage(
                                $"{Symbol} Первый стоп или изменение направления. Отменя и выставляем новый. Направление {Direction} цена = {PriceDeltaNow} ");
                            CurretPriceDelta = PriceDeltaNow;
                            CancelAndPlaceNewStopOrder(CurretPriceDelta, (int)newPos, Direction,
                                QuikConnector.getDecimalCount(LastPrice));
                        }

                        if (CurretPriceDelta != 0 /*&& newPos != 0*/ && prevDirection == Direction)
                        {
                            if ((Direction == Operation.Buy && PriceDeltaNow > CurretPriceDelta) ||
                                (Direction == Operation.Sell && PriceDeltaNow < CurretPriceDelta))
                            {
                                LogMessage(
                                    $"Уровень изменился. Новый ={PriceDeltaNow} Старый = {CurretPriceDelta}. Направление {Direction}");
                                CurretPriceDelta = PriceDeltaNow;
                                CancelAndPlaceNewStopOrder(CurretPriceDelta, (int)newPos, Direction,
                                    QuikConnector.getDecimalCount(LastPrice));
                            }
                        }

                    }
                    else
                    {
                        var localEMA = QuikConnector.GetEmaValueOrLastPrice(this, true, CandleInterval, EmaLength)
                            .Result;

                        //сменилось направление или с самого нуля стартуем
                        if (LastEmaValue == 0 || prevDirection != Direction)
                        {
                            LogMessage(
                                $"{Symbol} Первый стоп или изменение направления. Отменя и выставляем новый. Направление {Direction} цена = {localEMA} ");
                            LastEmaValue = localEMA;
                            //todo - заменить нулевые значения. Добавить в основной коннектор количество чисел после запятой
                            CancelAndPlaceNewStopOrder(LastEmaValue, (int)newPos, Direction, 0);
                        }

                        if (LastEmaValue != 0 /*&& newPos != 0*/ && prevDirection == Direction)
                        {
                            if ((Direction == Operation.Buy && localEMA > LastEmaValue) ||
                                (Direction == Operation.Sell && LastEmaValue < LastEmaValue))
                            {
                                LogMessage(
                                    $"Уровень EMA изменился. Новый ={localEMA} Старый = {LastEmaValue}. Направление {Direction}");
                                LastEmaValue = localEMA;
                                CancelAndPlaceNewStopOrder(LastEmaValue, (int)newPos, Direction,
                                    QuikConnector.getDecimalCount(LastPrice));
                            }
                        }

                    }
                }
            }

            CurrentPos = newPos;
            prevDirection = Direction;
            
        }

        // QuikConnector.PlaceStopOrder(PriceDelta, (int)newPos, Direction, Symbol, QuikConnector.getDecimalCount(LastPrice));
        private void CancelAndPlaceNewStopOrder(decimal price, int quantity, Operation operation,int numberRound)
        {
            TryToFindAndCancelStopOrder();
            QuikConnector.PlaceStopOrder(price, quantity, operation, Symbol, numberRound);
        }
        private void TryToFindAndCancelStopOrder()
        {
            var stoporder = QuikConnector.ActiveStopOrders.FirstOrDefault(s => s.Value.SecCode == Symbol).Value;

            if (stoporder != null)
                QuikConnector.CancelStopOrder(stoporder);

        }

        public void RefreshPosBot()
        {
            LastEmaValue = 0;
            LastPrice = 0;
         
        }

        public void Start()
        {
            if(Activated)
            Started = true;
        }
        public void Stop()
        {
            Started = false;
        }


    }
}
