﻿using System;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Threading;
using System.Configuration;

namespace opcua_extractor_net
{
    class Program
    {
        static void Main(string[] args)
        {
            string clientURL = ConfigurationManager.AppSettings["clientURL"];
            bool autoaccept = ConfigurationManager.AppSettings["autoaccept"] == "true";
            if (!uint.TryParse(ConfigurationManager.AppSettings["maxResults"], out uint maxResults))
            {
                throw new Exception("Invalid configuration: maxResults");
            }
            if (!int.TryParse(ConfigurationManager.AppSettings["pollingInterval"], out int pollingInterval))
            {
                throw new Exception("Invalid configuration: pollingInterval");
            }
            UAClient client = new UAClient(clientURL, autoaccept, pollingInterval, maxResults);
            client.Run().Wait();
            client.DebugBrowseDirectory(ObjectIds.ObjectsFolder);
            

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (sender, eArgs) =>
                {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
            }

            quitEvent.WaitOne(-1);
        }
    }
}
