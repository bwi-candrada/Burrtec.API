using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Data
{
    public class APIDBContext : DbContext
    {
        public APIDBContext(DbContextOptions<APIDBContext> options)
            : base(options)
        {
        }

        public DbSet<Domain.Entities.BulkOrder> BulkOrders { get; set; }
    }
}
