using System.Diagnostics.Tracing;

namespace HuaweiCloud.GaussDB;

sealed class GaussDBSqlEventSource : EventSource
{
    public static readonly GaussDBSqlEventSource Log = new();

    const string EventSourceName = "GaussDB.Sql";

    const int CommandStartId = 3;
    const int CommandStopId = 4;

    internal GaussDBSqlEventSource() : base(EventSourceName) {}

    // NOTE
    // - The 'Start' and 'Stop' suffixes on the following event names have special meaning in EventSource. They
    //   enable creating 'activities'.
    //   For more information, take a look at the following blog post:
    //   https://blogs.msdn.microsoft.com/vancem/2015/09/14/exploring-eventsource-activity-correlation-and-causation-features/
    // - A stop event's event id must be next one after its start event.

    [Event(CommandStartId, Level = EventLevel.Informational)]
    public void CommandStart(string sql) => WriteEvent(CommandStartId, sql);

    [Event(CommandStopId, Level = EventLevel.Informational)]
    public void CommandStop() => WriteEvent(CommandStopId);
}
