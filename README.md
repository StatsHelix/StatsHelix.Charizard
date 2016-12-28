# StatsHelix.Charizard

The StatsHelix Charizard web framework.

Example:

```csharp
using System;
using System.Threading.Tasks;

using StatsHelix.Charizard;
using static StatsHelix.Charizard.HttpResponse;

namespace TestApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var server = new HttpServer(new IPEndPoint(IPAddress.Loopback, 80), typeof(Program).Assembly);
            // server.UnexpectedException += e => Console.WriteLine(e);
            server.Run().Wait();
        }
    }

    [Controller]
    public class TestController
    {
        public static HttpResponse Static(HttpRequest req)
        {
            // Return any HttpResult (see that class for what's possible)
            return String("Success.");
        }

        public HttpResponse Sync(HttpRequest req)
        {
            // Charizard instantiates one controller object per request for instance actions
            return String("Success.");
        }

        public async Task<HttpResponse> Async(HttpRequest req)
        {
            // Async works
            return Task.FromResult(String("Success."));
        }
    }
}
```
