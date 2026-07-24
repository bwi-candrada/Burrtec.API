using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Domain.Entities
{
    public class BulkOrder
    {
        [Key]
        public int BulkOrderID { get; set; }
        public string ConfirmationID { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime CreatedDateTime { get; set; }
    }
}
