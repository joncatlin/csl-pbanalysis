using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using System.IO;
using System.Text.RegularExpressions;
using Akka.Event;

namespace csl_pbanalysis
{
    #region Message classes
    public class ReadDir
    {
        public ReadDir(string filename, string outputFilename)
        {
            Filename = filename;
            OutputFilename = outputFilename;
        }

        public string Filename { get; private set; }
        public string OutputFilename { get; private set; }
    }

    public class Read
    {
        public Read(string filename)
        {
            Filename = filename;
        }

        public string Filename { get; private set; }
    }

    public class Finished
    {
        public Finished(string filename, SortedDictionary<string, UniVerseFile> files)
        {
            Filename = filename;
            Files = files;
        }

        public string Filename { get; private set; }
        public SortedDictionary<string, UniVerseFile> Files { get; private set; }
    }

    #endregion

    public class FileReaderActor : ReceiveActor
    {
        #region Instance variables
        private string _programName;
        private readonly string FILE_COMMANDS =
            @"^\s*(?<command>(OPEN|OPENPATH|READ[VULT]*|CALL\s+OPEN\.FILE\.SUB|READSEQ|WRITE[VULT]*|WRITESEQ|EXECUTE|CHAIN|[\$]*INCLUDE|MATREAD[U]?|MATWRITE[U]?|CALL|ENTER|PROGRAM|SYSTEM|OPENSEQ))[\s|\(]+.*\n";
        private readonly string OPEN_COMMAND =
            @"^\s*(OPEN|OPENSEQ|OPENPATH)\s+(?<filename>.+)\s+TO\s+(?<to>[^\s\(\)]+)(?<isarray>\(\S+\))?";
//            @"^\s*(OPEN|OPENSEQ|OPENPATH)\s+(?<filename>[^\s]+)\s+TO\s+(?<to>[^\s\(\)]+)(?<isarray>\(\S+\))?";
        private readonly string OPEN_FILE_SUB_COMMAND =
            @"^\s*(CALL\s+OPEN\.FILE\.SUB)[\s|\(]+(?<filename>.*)\s*,\s*(?<to>\S+)\s*,";
        private readonly string READ_COMMAND =
            @"^\s*(?<command>MATREAD|READSEQ|READ[VUL]*)\s+(?<variable>.+)\s*FROM\s*(?<handle>[\.'\d\w]+)(?<isarray>\(\S+\))?\s*,?.*\n";
//        private readonly string WRITE_COMMAND =
//            @"\s*(?<command>WRITE[VUL]*|WRITESEQ)\s+((?<variable>\S+)\s*)\s*(TO|ON)+\s*((?<handle>[\.'\d\w]+)\s*,)?\s*(?<recordid>\S*)\s*(ELSE.*\n|THEN.*\n|.*\n)";
//        private readonly string WRITE_COMMAND =
//            @"\s* (?<command>WRITE[VUL]*|WRITESEQ)\s+((?<variable>.+)\s*)\s* (TO|ON)+\s* ((?<handle>[\.'\d\w]+)\s*,)?\s*(?<recordid>\S*)\s*(ELSE.*\n|THEN.*\n|.*\n)";
        private readonly string WRITE_COMMAND =
            @"^\s*(?<command>MATWRITE|WRITESEQ|WRITE[VUL]*)\s+(?<variable>.+)\s*(ON|TO)+\s* ((?<handle>[\.'\d\w]+)\s*)(?<isarray>\(\S+\))?,?.*\n";





        private readonly string EXTERNAL_DEPENDENCY_COMMAND =
            @"\s*(?<command>CALL|CHAIN|[\$]*INCLUDE|EXECUTE|ENTER|PROGRAM|SYSTEM)\s+(?<external_link>[^\(\s]+)[\s|\(]*";


        private Regex rgxFileCommands;
        private Regex rgxOpenCommand;
        private Regex rgxReadCommand;
        private Regex rgxWriteCommand;
        private Regex rgxExternalCommand;
        private Regex rgxCallOpenFileSub;
        private ILoggingAdapter _log;

        private readonly string DEFAULT_FILEHANDLE = "__defaultFileHandle";

        // Dictionary containing the open files found in the analysis
        SortedDictionary<string, string> handleToFilenameLookup;
        SortedDictionary<string, UniVerseFile> files;
        #endregion


        public FileReaderActor()
        {
            _log = Context.GetLogger();

            _log.Info("FileReaderActor HI JON");

            // Initialize the patterns used to scan the file for metrics
            rgxFileCommands = new Regex(FILE_COMMANDS, RegexOptions.Compiled | RegexOptions.Multiline);
            rgxOpenCommand = new Regex(OPEN_COMMAND, RegexOptions.Compiled);
            rgxReadCommand = new Regex(READ_COMMAND, RegexOptions.Compiled);
            rgxWriteCommand = new Regex(WRITE_COMMAND, RegexOptions.Compiled);
            rgxExternalCommand = new Regex(EXTERNAL_DEPENDENCY_COMMAND, RegexOptions.Compiled);
            rgxCallOpenFileSub = new Regex(OPEN_FILE_SUB_COMMAND, RegexOptions.Compiled);


            

            Receive<Read>(msg => ReadFile(msg));
        }


        private void ReadFile(Read msg)
        {
            _programName = msg.Filename;

            string readText = File.ReadAllText(msg.Filename);

            _log.Info("Read fileName={0}", msg.Filename);
            handleToFilenameLookup = new SortedDictionary<string, string>();
            files = new SortedDictionary<string, UniVerseFile>();

            FindCommands(readText);

            Sender.Tell(new Finished(msg.Filename, files));
        }

       
        private void FindCommands(string text)
        {
            var matches = rgxFileCommands.Matches(text);
            foreach (Match match in matches)
            {
                var groups = match.Groups;
                string command = groups["command"].Value.Trim();
                switch (command.ToUpper())
                {
                    case "OPEN":
                    case "OPENSEQ":
                    case "OPENPATH":
                        getOpen(groups[0].Value);
                        break;

                    case "CALL OPEN.FILE.SUB":
                        getCallOpenFileSub(groups[0].Value);
                        break;

                    case "*":
                    case "!":
                    case "PRINT":
                        // Ignore
                        break;

                    case "CALL":
                    case "CHAIN":
                    case "INCLUDE":
                    case "$INCLUDE":
                    case "EXECUTE":
                    case "ENTER":
                    case "PROGRAM":
                    case "SYSTEM":
                        getExternal(groups[0].Value);
                        break;

                    case "READ":
                    case "READV":
                    case "READVU":
                    case "READVL":
                    case "READU":
                    case "READL":
                    case "MATREAD":
                    case "MATREADU":
                    case "READT":
                    case "READSEQ":
                        getReadWrite(rgxReadCommand, groups[0].Value);
                        break;
                    case "WRITE":
                    case "WRITEV":
                    case "WRITEU":
                    case "WRITEL":
                    case "WRITEVU":
                    case "WRITEVL":
                    case "MATWRITE":
                    case "MATWRITEU":
                    case "WRITET":
                    case "WRITESEQ":
                        getReadWrite(rgxWriteCommand, groups[0].Value);
                        break;
                    default:
                        _log.Error("Unknown command found. Command ='{0}' this must be corrected or else the analysis will produce wrong results.", command);
                        break;

                }
            }
        }

        private void getOpen(string text)
        {
            // Parse the string and pick out the relevant information
            var match = rgxOpenCommand.Match(text);
            var groups = match.Groups;
            string dict = groups["dict"].Value.Trim();
            string filename = groups["filename"].Value.Trim();
            string isArray = groups["isarray"].Value.Trim();
            string to = groups["to"].Value.Trim();

            _log.Debug("Found OPEN|OPENSEQ statement for filename {0}, dict: {1}, handle: {2}", filename, dict, to);

            // Check to see if somethign matched otherwise there is an error
            if (match.Success)
            {
                // Ignore the filename if it is an array
                if (!isArray.Equals(""))
                {
                    // Skip this statement as it is using an array for the file handle
                    _log.Info("Found array as handle so skipping statement. The statement found is {0}", text);
                    return;
                }

                // Add the file handle and the file name to the dictionary of open files found
                if (to.Equals(""))
                {
                    // This is a reference to a default file which we need to update so that any read or writes not using a handle will use this
                    if (!handleToFilenameLookup.TryAdd(DEFAULT_FILEHANDLE, filename))
                    {
                        handleToFilenameLookup[DEFAULT_FILEHANDLE] = filename;
                    }
                }

                // Add to the list of handles or update if already exists
                if (!handleToFilenameLookup.TryAdd(to, filename))
                {
                    handleToFilenameLookup[to] = filename;
                }

                // Add the name of the file to the list of files found, ignore if it already exists
                var universeFile = new UniVerseFile(dict, filename);
                files.TryAdd(filename, universeFile);
            }
            else
            {
                _log.Error("Expecting to match an OPEN statement but none found. Something is wrong with the match pattern for the OPEN statement. Text being matched is '{0}", text);
            }
        }

        private void getCallOpenFileSub(string text)
        {
            // Parse the string and pick out the relevant information
            var match = rgxCallOpenFileSub.Match(text);
            var groups = match.Groups;
            string filename = groups["filename"].Value;
            string to = groups["to"].Value;

            _log.Debug("Found CALL OPEN.FILE.SUB statement for filename {0}, handle: {1}", filename, to);

            // Check to see if somethign matched otherwise there is an error
            if (match.Success)
            {
                // Add the file handle and the file name to the dictionary of open files found
                if (to.Equals(""))
                {
                    // This is a reference to a default file which we need to update so that any read or writes not using a handle will use this
                    if (!handleToFilenameLookup.TryAdd(DEFAULT_FILEHANDLE, filename))
                    {
                        handleToFilenameLookup[DEFAULT_FILEHANDLE] = filename;
                    }
                }

                // Add to the list of handles or update if already exists
                if (!handleToFilenameLookup.TryAdd(to, filename))
                {
                    handleToFilenameLookup[to] = filename;
                }

                // Add the name of the file to the list of files found, ignore if it already exists
                var universeFile = new UniVerseFile("", filename);
                files.TryAdd(filename, universeFile);
            }
            else
            {
                _log.Error("Expecting to match an OPEN statement but none found. Something is wrong with the match pattern for the OPEN statement. Text being matched is '{0}", text);
            }
        }

        private void getReadWrite(Regex pattern, string text)
        {
            // Parse the string and pick out the relevant information
            var match = pattern.Match(text);
            var groups = match.Groups;
            string command = groups["command"].Value.Trim();
            string variable = groups["variable"].Value.Trim();
            string handle = groups["handle"].Value.Trim();
            string isArray = groups["isarray"].Value.Trim();

            _log.Debug("Found {0} statement for handle {1}, variable: {2}", command, handle, variable);

            // Check to see if somethign matched otherwise there is an error
            if (match.Success)
            {
                string filename;

                // Ignore the filename if it is an array
                if (!isArray.Equals(""))
                {
                    // Skip this statement as it is using an array for the file handle
                    _log.Info("Found array as handle so skipping statement. The statement found is {0}", text);
                    return;
                }


                // Check for use of the default file
                if (handle.Equals(""))
                {
                    if (!handleToFilenameLookup.ContainsKey(DEFAULT_FILEHANDLE))
                    {
                        _log.Error("Cannot find DEFAULT file handle in program {0}, line in file is: {1}", handle, _programName, text);
                    }
                    else
                    {
                        // Use default file handle
                        filename = handleToFilenameLookup[DEFAULT_FILEHANDLE];
                    }
                }
                else
                {
                    if (!handleToFilenameLookup.ContainsKey(handle))
                    {
                        _log.Error("Cannot find file handle named '{0}' in program {1}, line in file is: {2}", handle, _programName, text);
                    }
                    else
                    {
                        // Find the filename for the handle and update the number of reads
                        filename = handleToFilenameLookup[handle];

                        if (filename.Equals(""))
                        {
                            _log.Error("Cannot find filename from file.variable on READ command. READ statement: {0}. File.variable found is: {1}", text, handle);
                        }
                        else
                        {
                            switch (command.ToUpper())
                            {
                                case "READ":
                                case "READV":
                                case "READVU":
                                case "READVL":
                                case "READU":
                                case "READL":
                                case "READSEQ":
                                case "MATREAD":
                                    files[filename].ReadCount++;
                                    break;
                                case "WRITE":
                                case "WRITEV":
                                case "WRITEU":
                                case "WRITEL":
                                case "WRITEVU":
                                case "WRITEVL":
                                case "WRITESEQ":
                                case "MATWRITE":
                                    files[filename].WriteCount++;
                                    break;
                                default:
                                    _log.Error("Expecting READ or WRITE type command and found: {0} in line {1}", command, text);
                                    break;
                            }
                        }
                    }
                }
            }
            else
            {
                _log.Error("Expecting to match a READ/WRITE statement but none found. Something is wrong with the match pattern for the READ statement. Text being matched is '{0}", text);
            }
        }


        private void getExternal(string text)
        {

            // Parse the string and pick out the relevant information
            var match = rgxExternalCommand.Match(text);
            var groups = match.Groups;
            string command = groups["command"].Value.Trim();
            string externalLink = groups["external_link"].Value.Trim();

            _log.Debug("Found external statement for command: {0}, external_link: {1}", command, externalLink);

            // Check to see if somethign matched otherwise there is an error
            if (match.Success)
            {
                if (externalLink.Equals(""))
                {
                    _log.Error("Cannot find external link for command, statement: {0}. command found is: {1}", text, command);
                }
                else
                {
                    switch (command.ToUpper())
                    {
                        case "CALL":
                        case "CHAIN":
                        case "INCLUDE":
                        case "$INCLUDE":
                        case "EXECUTE":
                        case "ENTER":
                        case "PROGRAM":
                        case "SYSTEM":
                            // TODO add action for this item here
                            break;
                        default:
                            _log.Error("Expecting External command and found: {0} in line {1}", command, text);
                            break;
                    }
                }


/*







                // Add the file handle and the file name to the dictionary of open files found
                if (to.Equals(""))
                {
                    // This is a reference to a default file which we need to update so that any read or writes not using a handle will use this
                    if (!handleToFilenameLookup.TryAdd(DEFAULT_FILEHANDLE, filename))
                    {
                        handleToFilenameLookup[DEFAULT_FILEHANDLE] = filename;
                    }
                }

                // Add to the list of handles or update if already exists
                if (!handleToFilenameLookup.TryAdd(to, filename))
                {
                    handleToFilenameLookup[to] = filename;
                }

                // Add the name of the file to the list of files found, ignore if it already exists
                var universeFile = new UniVerseFile(dict, filename);
                files.TryAdd(filename, universeFile);
*/
            }
            else
            {
                _log.Error("Expecting to match an external statement but none found. Something is wrong with the match pattern for the OPEN statement. Text being matched is '{0}", text);
            }
        }



    }
}
