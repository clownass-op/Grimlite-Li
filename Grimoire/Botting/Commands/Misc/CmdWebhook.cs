using Grimoire.Game;
using Grimoire.UI;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Grimoire.Botting.Commands.Misc
{
    public class CmdWebhook : IBotCommand
    {
        public string Message
        {
            get;
            set;
        }

        public async Task Execute(IBotEngine instance)
        {
            string msg = (instance.IsVar(this.Message) ? Configuration.Tempvariable[instance.GetVar(this.Message)] : this.Message);
            string webhookUrl = AccountManager.Instance?.tbWebhook?.Text?.Trim();

            if (string.IsNullOrEmpty(webhookUrl))
            {
                return;
            }

            try
            {
                var payload = new { content = msg };
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await AccountManager.CreateHttpClient().PostAsync(webhookUrl, content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception)
            {
                // Ignore errors
            }
        }

        public override string ToString()
        {
            return "Send webhook : " + Message;
        }
    }
}
