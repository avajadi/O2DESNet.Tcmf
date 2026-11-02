using O2DESNet.Distributions;
using O2DESNet.Standard;
using O2DESNet.Tcmf;
using System;

namespace O2DESNet.Tcmf.Demos
{
    /// <summary>
    /// Trucks arrive at a container terminal gate, wait in a queue, are loaded by a crane and leave.
    /// Each step is published as a TCMF message: LocationState arrived_to the gate, ServiceState commenced
    /// and completed for the loading, and LocationState departed_from the terminal.
    /// </summary>
    public class TcmfContainerTerminal : Sandbox
    {
        #region Static Properties
        public double HourlyArrivalRate { get; private set; }
        public double HourlyLoadingRate { get; private set; }
        public string Terminal { get; private set; } = TcmfUrn.Location("terminal", "SEGOT", "Skandiahamnen");
        public string Gate { get; private set; } = TcmfUrn.Location("terminal", "SEGOT", "Skandiahamnen", "gate_1");
        public string LoadingService { get; private set; } = TcmfUrn.Service("loading", "container", "crane");
        #endregion

        #region Dynamic Properties
        public TcmfReporter Reporter { get; private set; }
        public int NDeparted { get; private set; }
        public double AvgNQueueing { get { return GateQueue.AvgNQueueing; } }
        public double CraneUtilization { get { return Crane.UtilServing; } }

        private readonly IGenerator Generator;
        private readonly IQueue GateQueue;
        private readonly IServer Crane;
        #endregion

        #region Events
        private void Arrive()
        {
            var plate = string.Format("ABC{0:000}", Generator.Count);
            var truck = new TcmfLoad(
                TcmfUrn.ReferenceObject("truck", "registration_plate", plate),
                TcmfUrn.Grouping(GroupingType.Truck, "registration_plate", plate));
            GateQueue.RqstEnqueue(truck);
        }

        private void Depart(ILoad truck)
        {
            Crane.Depart(truck);
            Reporter.Departed(TcmfStandardExtensions.ReferenceObjectOf(truck), Terminal, TcmfStandardExtensions.GroupingOf(truck));
            NDeparted++;
        }
        #endregion

        /// <param name="arrRate">Hourly truck arrival rate</param>
        /// <param name="loadRate">Hourly loading rate of the crane</param>
        /// <param name="craneCapacity">Trucks the crane serves at once</param>
        /// <param name="sink">Where the TCMF messages go</param>
        /// <param name="seed">Random seed, also used for reproducible message ids</param>
        /// <param name="calendarStart">Calendar time of simulation start; default 2026-01-01 06:00 UTC</param>
        public TcmfContainerTerminal(double arrRate, double loadRate, int craneCapacity, ITcmfSink sink,
            int seed = 0, DateTime? calendarStart = null)
            : base(seed)
        {
            HourlyArrivalRate = arrRate;
            HourlyLoadingRate = loadRate;

            Reporter = new TcmfReporter(this, sink, TcmfUrn.User("RISE", "o2des_sim"),
                TcmfReporter.AnchorClock(calendarStart ?? new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc)))
            {
                MessageIdFactory = TcmfReporter.SeededMessageIds(seed),
            };

            Generator = AddChild(new Generator(new Generator.Statics
            {
                InterArrivalTime = rs => Exponential.Sample(rs, TimeSpan.FromHours(1 / HourlyArrivalRate))
            }, DefaultRS.Next()));

            GateQueue = AddChild(new Queue(double.PositiveInfinity, DefaultRS.Next(), id: "GateQueue"));

            Crane = AddChild(new Server(new Server.Statics
            {
                Capacity = craneCapacity,
                ServiceTime = (rs, load) => Exponential.Sample(rs, TimeSpan.FromHours(1 / HourlyLoadingRate)),
            }, DefaultRS.Next(), id: "Crane"));

            // TCMF hooks first, so "completed" is published before the departure below
            GateQueue.ReportTcmf(Reporter, Gate);
            Crane.ReportTcmf(Reporter, LoadingService, Terminal);

            Generator.OnArrive += Arrive;
            GateQueue.OnEnqueued += Crane.RqstStart;
            Crane.OnStarted += GateQueue.Dequeue;
            Crane.OnReadyToDepart += Depart;

            Generator.Start();
        }

        public override void Dispose() { }
    }
}
