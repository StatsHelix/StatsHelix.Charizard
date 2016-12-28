using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard.Backend
{
    public interface IRequestDispatcher
    {
        Task<HttpResponse> Dispatch(HttpRequest request);
    }
}
