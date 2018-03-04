using System;
using System.Collections.Generic;
using System.Text;

namespace csl_pbanalysis
{
    public class UniVerseFile
    {
        public UniVerseFile(string dict, string filename)
        {
            Filename = filename;
            Dict = dict;
        }

        public string Filename { get; private set; }
        public string Dict { get; private set; }
        public int ReadCount { get; set; }
        public int WriteCount { get; set; }
    }
}
