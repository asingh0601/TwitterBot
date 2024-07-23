using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace TwitterBot
{
	internal class Bot
	{
		public string TwitterUserName { get; set; }
		public string TwitterEmail { get; set; }
		public string TwitterPassword { get; set; }
		[NotMapped ]
		public long? ProcessId { get; set; }
		[NotMapped]
		public string UserDataDirectory { get; set; }
		[NotMapped]
		public bool LoginSuccessful { get; set; }
		[NotMapped]
		public bool MasterUserDataExists { get; set; }
	}
}
