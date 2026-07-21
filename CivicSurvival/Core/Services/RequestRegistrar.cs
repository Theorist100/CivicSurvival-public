using System.Threading;

namespace CivicSurvival.Core.Services
{
    public static class RequestRegistrar
    {
        private static int s_NextRequestId;

        public static int NextRequestId() => Interlocked.Increment(ref s_NextRequestId);

        public static void RebaseAfterLoad(int maxRestoredRequestId)
        {
            if (maxRestoredRequestId <= 0)
                return;

            while (true)
            {
                int current = Volatile.Read(ref s_NextRequestId);
                if (current >= maxRestoredRequestId)
                    return;

                if (Interlocked.CompareExchange(ref s_NextRequestId, maxRestoredRequestId, current) == current)
                    return;
            }
        }
    }
}
