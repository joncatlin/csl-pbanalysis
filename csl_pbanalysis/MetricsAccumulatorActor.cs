using System;
using System.IO;
using Akka.Actor;
using Akka.Routing;
using Akka.Event;
using System.Collections.Generic;
using System.Text;

namespace csl_pbanalysis
{
    class MetricsAccumulatorActor : ReceiveActor
    {

        #region Local variables
        #endregion

        private IActorRef _fileReadActorDispatcher;
        private ILoggingAdapter _log;
        private SortedDictionary<string, SortedDictionary<string, UniVerseFile>> _files;
        private int _foundFilesCount = 0;
        private string _outputFilename;

        private readonly string NODES_FILENAME = "nodes.csv";
        private readonly string EDGES_FILENAME = "edges.csv";

        public MetricsAccumulatorActor()
        {
            _log = Context.GetLogger();

            // Initialize the storage to hols all the results
            _files = new SortedDictionary<string, SortedDictionary<string, UniVerseFile>>();

           _fileReadActorDispatcher = Context.ActorOf(Props.Create(() =>
                new FileReaderActor())
                .WithRouter(new RoundRobinPool(10)));

            Receive<ReadDir>(msg => ReadDir(msg));
            Receive<Finished>(msg => FileReaderFinished(msg));
        }

        private void ReadDir(ReadDir msg)
        {
            // Save the name fo the file to hold the results once all actors are finished
            _outputFilename = msg.OutputFilename;

            _log.Info("Checking directory named: {0} for files", msg.Filename);

            foreach (string file in Directory.EnumerateFiles(msg.Filename, "*", SearchOption.AllDirectories))
            {
                _log.Info("Found file named: {0}", file);
                _foundFilesCount++;

                // Start an actor in the pool to deal with the new file
                _fileReadActorDispatcher.Tell(new Read(file));
            }

            _log.Info("Found {0} files in directory {1}", _foundFilesCount, msg.Filename);
        }

        private void FileReaderFinished(Finished msg)
        {
            var programName = Path.GetFileName(msg.Filename);
            _files.Add(programName, msg.Files);

            // Check to see if all actors have finished
            if (_files.Count == _foundFilesCount)
            {
                _log.Info("All actors finished");
                OutputNodesAndEdges();
                //OutputResults();
            }
        }


        private void OutputNodesAndEdges()
        {
            string PROGRAM = "PROGRAM";
            string FILE = "FILE";

            var nodesFilename = Path.Combine(@"c:\temp", NODES_FILENAME);
            var edgesFilename = Path.Combine(@"c:\temp", EDGES_FILENAME);

            Dictionary<string, string> nodes = new Dictionary<string, string>(5000);
            Dictionary<string, string> edges = new Dictionary<string, string>(5000);

            // Foreach Pick Basic program found
            foreach (var programKVP in _files)
            {
                var programName = programKVP.Key;

                // Create a node
                if (!nodes.TryAdd(programName, PROGRAM))
                {
                    _log.Info("Duplicate program found when outputting nodes and edges, program name: {0}", programName);
                }

                // For each file connection found in the program
                foreach (var universeKVP in programKVP.Value)
                {
                    // Ensure the file is in the list of nodes
                    nodes.TryAdd(universeKVP.Key, FILE);

                    // Create the relationship in the list of edges
                    if (universeKVP.Value.ReadCount > 0) edges.TryAdd(universeKVP.Key, programName);
                    if (universeKVP.Value.WriteCount > 0) edges.TryAdd(programName, universeKVP.Key);
                }
            }

            // Output the list of nodes
            FileStream fs = null;
            try
            {
                fs = new FileStream(nodesFilename, FileMode.Create);
                using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    // Output the header for the file
                    string header = "ID,LABEL,TYPE";
                    sw.WriteLine(header);

                    // Foreach Pick Basic program found
                    foreach (var nodeKVP in nodes)
                    {
                        sw.WriteLine("\"{0}\",\"{1}\",{2}", nodeKVP.Key.Trim().Replace("\"", String.Empty).Replace("'", String.Empty),
                            nodeKVP.Key.Trim().Replace("\"", String.Empty).Replace("'", String.Empty),
                            nodeKVP.Value.Trim().Replace("\"", String.Empty).Replace("'", String.Empty));
                    }
                }
            }
            finally
            {
                // Tidy up
                if (fs != null)
                    fs.Close();
            }


            // Output the list of nodes
            try
            {
                fs = new FileStream(edgesFilename, FileMode.Create);
                using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    // Output the header for the file
                    string header = "SOURCE,TARGET";
                    sw.WriteLine(header);

                    // Foreach Pick Basic program found
                    foreach (var edgeKVP in edges)
                    {
                        sw.WriteLine("\"{0}\",\"{1}\"", edgeKVP.Key.Trim().Replace("\"", String.Empty).Replace("'", String.Empty),
                            edgeKVP.Value.Trim().Replace("\"", String.Empty).Replace("'", String.Empty));
                    }
                }
            }
            finally
            {
                // Tidy up
                if (fs != null)
                    fs.Close();
            }
            // Finished!
            Environment.Exit(0);
        }


    }
}
