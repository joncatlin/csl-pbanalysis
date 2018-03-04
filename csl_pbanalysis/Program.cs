using System;
using Akka.Actor;
using Akka.Configuration;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace csl_pbanalysis
{
    class Program
    {
        // Names of all the environment variables needed by the application
        static readonly string ENV_DIRNAME = "DIR_NAME";
        static readonly string ENV_OUTPUT_FILENAME = "OUTPUT_FILENAME";
        static string _dirName;
        static string _outputFilename;

        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("USAGE: Two arguments expected. First arg is the directory to look for Pick Basic files in. The second arg is the name of the output file. Press any key to terminate the program.");
                Console.ReadKey();
                Environment.Exit(0);
            } else
            {
                _dirName = args[0];
                _outputFilename = args[1];
            }

            // Get the configuration of the akka system
            var config = ConfigurationFactory.ParseString(GetConfiguration());

            // Create the container for all the actors
            var pbanalysisActorSystem = ActorSystem.Create("pbanalysis", config);

            // Create the that will collected all of the metrics from the files in a given directory
            Props maProps = Props.Create(() => new MetricsAccumulatorActor());
            IActorRef maActor = pbanalysisActorSystem.ActorOf(maProps, "metricsAccumulatorActor");
            maActor.Tell(new ReadDir(_dirName, _outputFilename));

            // Wait until actor system terminated
            pbanalysisActorSystem.WhenTerminated.Wait();
      
        }


        private static string GetConfiguration()
        {
            string config = @"
                akka {  
                    stdout-loglevel = ERROR
                    loglevel = ERROR
#                    loggers = [""Akka.Logger.NLog.NLogLogger, Akka.Logger.NLog""]
                    log-config-on-start = on

                    actor
                    {
                      debug
                      {
                        receive = on      # log any received message
                        autoreceive = on  # log automatically received messages, e.g. PoisonPill
                        lifecycle = on    # log actor lifecycle changes
                        event-stream = on # log subscription changes for Akka.NET event stream
                        unhandled = on    # log unhandled messages sent to actors
                      }
                    }
                  }
                }
            ";

            return config;
        }


    }
}

