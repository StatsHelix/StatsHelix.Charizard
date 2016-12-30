# StatsHelix.Charizard

The StatsHelix Charizard web framework.

## Example

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
        // GET /Test/Static HTTP/1.1
        public static HttpResponse Static(HttpRequest req)
        {
            // Return any HttpResult (see that class for what's possible)
            return String("Success.");
        }

        // GET /Test/Sync HTTP/1.1
        public HttpResponse Sync(HttpRequest req)
        {
            // Charizard instantiates one controller object per request for instance actions
            return String("Success.");
        }
        
        // GET /Test/Async HTTP/1.1
        public async Task<HttpResponse> Async(HttpRequest req)
        {
            // Async works
            return await Task.FromResult(String("Success."));
        }
        
        // GET /Test/Params?name=Hi&id=12 HTTP/1.1
        public HttpResponse Params(string name, int id)
        {
            // We can use parameters
            return Json(new FooType() { Name: name, Id: id });
            
            // Response:
            // Content-Type: text/json
            // { "Name": "Hi", "Id": 12 }
        }
        
        // POST /Test/Post HTTP/1.1
        // POST-Body: 
        // { "Name": "Hello", "Id": 13 }
        public HttpResponse Post(FooType foo) 
        {
            // Post bodies are JSON-encoded, so they are easy to generate
            // from any client-side language. Deep objects are no problems, 
            // anything that can be handled by Newtonsoft.Json is fine. 
            // We don't support HTML-Forms, since they are - on our opionion - outdated. 
            return String("foo.Name: " + foo.Name + " --- foo.Id: " + foo.Id);
        }
        
        // Dummy type for demonstration
        class FooType
        {
            public string Name { get; set; }
            public int Id { get; set; }
        }
    }
}
```
