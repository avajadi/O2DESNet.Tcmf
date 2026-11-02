using O2DESNet.Standard;
using System;
using System.Collections.Generic;

namespace O2DESNet.Tcmf
{
    /// <summary>
    /// A load that knows how it is identified in TCMF: its reference object and the groupings (timelines) it belongs to.
    /// </summary>
    public interface ITcmfLoad : ILoad
    {
        /// <summary>"tcmf:reference_object:..."</summary>
        string ReferenceObject { get; }
        /// <summary>"tcmf:grouping:..." entries, see <see cref="TcmfUrn.Grouping"/>.</summary>
        IReadOnlyList<string> Grouping { get; }
    }

    /// <summary>A <see cref="Load"/> carrying its TCMF identifiers.</summary>
    public class TcmfLoad : Load, ITcmfLoad
    {
        public string ReferenceObject { get; private set; }
        public IReadOnlyList<string> Grouping { get; private set; }

        public TcmfLoad(string referenceObject, params string[] grouping)
        {
            ReferenceObject = referenceObject ?? throw new ArgumentNullException(nameof(referenceObject));
            Grouping = Array.AsReadOnly(grouping ?? new string[0]);
        }

        public override string ToString() { return ReferenceObject; }
    }

    /// <summary>
    /// Hooks the events of the standard components up to a <see cref="TcmfReporter"/>.
    /// </summary>
    public static class TcmfStandardExtensions
    {
        /// <summary>
        /// Publishes a ServiceState "commenced" when the server starts a load and "completed" when the load is ready to depart.
        /// </summary>
        public static TServer ReportTcmf<TServer>(this TServer server, TcmfReporter reporter, string service,
            Func<ILoad, string> referenceObject, Func<ILoad, IEnumerable<string>> grouping, string location = null)
            where TServer : IServer
        {
            server.OnStarted += load => reporter.ServiceCommenced(service, grouping(load), referenceObject(load), location);
            server.OnReadyToDepart += load => reporter.ServiceCompleted(service, grouping(load), referenceObject(load), location);
            return server;
        }

        /// <summary>Same as above for servers whose loads are <see cref="ITcmfLoad"/>.</summary>
        public static TServer ReportTcmf<TServer>(this TServer server, TcmfReporter reporter, string service, string location = null)
            where TServer : IServer
        {
            return server.ReportTcmf(reporter, service, ReferenceObjectOf, GroupingOf, location);
        }

        /// <summary>Publishes a LocationState "arrived_to" <paramref name="location"/> when a load is enqueued.</summary>
        public static TQueue ReportTcmf<TQueue>(this TQueue queue, TcmfReporter reporter, string location,
            Func<ILoad, string> referenceObject, Func<ILoad, IEnumerable<string>> grouping)
            where TQueue : IQueue
        {
            queue.OnEnqueued += load => reporter.Arrived(referenceObject(load), location, grouping(load));
            return queue;
        }

        /// <summary>Same as above for queues whose loads are <see cref="ITcmfLoad"/>.</summary>
        public static TQueue ReportTcmf<TQueue>(this TQueue queue, TcmfReporter reporter, string location)
            where TQueue : IQueue
        {
            return queue.ReportTcmf(reporter, location, ReferenceObjectOf, GroupingOf);
        }

        public static string ReferenceObjectOf(ILoad load)
        {
            return AsTcmfLoad(load).ReferenceObject;
        }

        public static IEnumerable<string> GroupingOf(ILoad load)
        {
            return AsTcmfLoad(load).Grouping;
        }

        private static ITcmfLoad AsTcmfLoad(ILoad load)
        {
            if (load is ITcmfLoad tcmfLoad) return tcmfLoad;
            throw new InvalidOperationException(string.Format(
                "Load {0} does not implement ITcmfLoad; use the overload that takes referenceObject and grouping selectors", load));
        }
    }
}
