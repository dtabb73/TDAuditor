using System;
using System.IO;
using System.Xml;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace TDAuditor
{
    static class Program
    {
        static void Main(string[] args)
        {
            // Use periods to separate decimals
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Console.WriteLine("TDAuditor: Quality metrics for top-down proteomes");
            Console.WriteLine("David L. Tabb, University Medical Center of Groningen");
            Console.WriteLine("beta version 20250403");
	    Console.WriteLine("--MGF Read MGF file(s) produced by ProSight Proteome Discoverer.");
	    Console.WriteLine("--CC  Write largest connected component graph for GraphViz.");
	    Console.WriteLine("--DN  Write de novo sequence tag graphs for GraphViz.");

            /*
              TODO: Would like to have the following:
              -What is the max resolution seen for MS scans?
              -What is the max resolution seen for MSn scans?
            */

	    var ReadMGFnotMSAlign = false;
	    var WriteConnectedComponents = false;
	    var WriteDeNovoTags = false;
	    foreach (var item in args)
	    {
		switch(item)
		{
		    case "--MGF":
			ReadMGFnotMSAlign = true;
			break;
		    case "--CC":
			WriteConnectedComponents = true;
			break;
		    case "--DN":
			WriteDeNovoTags = true;
			break;
		    default:
			Console.Error.WriteLine("\tError: I don't understand this argument: {0}.", item);
			break;
		}
	    }
            var CWD = Directory.GetCurrentDirectory();
            const string mzMLPattern = "*.mzML";
            var mzMLs = Directory.GetFiles(CWD, mzMLPattern);
            const string msAlignPattern = "*ms2.msalign";
            var msAligns = Directory.GetFiles(CWD, msAlignPattern);
            const string mgfPattern = "*.mgf";
            var MGFs = Directory.GetFiles(CWD, mgfPattern);
            var Raws = new LCMSMSExperiment();
            var RawsRunner = Raws;
            string Basename;
	    Stopwatch Timer = new Stopwatch();
	    TimeSpan Duration;
	    Timer.Start();
            Console.WriteLine("\nImporting from mzML files...");
            foreach (var current in mzMLs)
            {
                Basename = Path.GetFileNameWithoutExtension(current);
                Console.WriteLine("\tReading mzML {0}", Basename);
                RawsRunner.Next = new LCMSMSExperiment();
                var FileSpec = Path.Combine(CWD, current);
                var XMLfile = XmlReader.Create(FileSpec);
                RawsRunner = RawsRunner.Next;
                RawsRunner.SourceFile = Basename;
                RawsRunner.ReadFromMZML(XMLfile);
                RawsRunner.ParseScanNumbers();
            }
	    Timer.Stop();
	    Duration = Timer.Elapsed;
	    Console.WriteLine("\tTime for mzML reading: {0}",Duration.ToString());
	    Timer.Reset();
	    Timer.Start();
	    if (ReadMGFnotMSAlign == true)
	    {
		Console.WriteLine("\nImporting from ProSight PD MGF...");
		foreach (var current in MGFs)
		{
		    Basename = Path.GetFileNameWithoutExtension(current);
		    Console.WriteLine("\tReading MGF {0}", Basename);
		    Raws.ReadFromMGF(current);
		}
		Raws.UpdateAllmsAlignStats();
	    }
	    else
	    {
		// TODO: The following will run into a problem if user has created a conjoint ms2.msalign file in TopPIC
		Console.WriteLine("\nImporting from msAlign files...");
		foreach (var current in msAligns)
		{
		    Basename = Path.GetFileNameWithoutExtension(current);
		    Console.WriteLine("\tReading msAlign {0}", Basename);
		    var SourcemzML = SniffMSAlignForSource(current);
		    var CorrespondingRaw = Raws.Find(SourcemzML);
		    if (CorrespondingRaw == null)
		    {
			Console.Error.WriteLine("\tWARNING: {0} could not be matched to an mzML title.", Basename);
		    }
		    else
		    {
			CorrespondingRaw.ReadFromMSAlign(current);
		    }
		}
		Raws.UpdateAllmsAlignStats();
		/*
		  TODO: we should really check to see if any of the mzMLs
		  lack corresponding msAlign files.
		*/
	    }
	    Timer.Stop();
	    Duration = Timer.Elapsed;
	    Console.WriteLine("\tTime for deconvolution reading: {0}",Duration.ToString());
	    Timer.Reset();
	    Timer.Start();
            Console.WriteLine("\nSeeking MS/MS scan pairs with many shared masses...");
            Raws.FindSimilarSpectraWithinRaw(WriteConnectedComponents);
	    Timer.Stop();
	    Duration = Timer.Elapsed;
	    Console.WriteLine("\tTime for similarity detection: {0}",Duration.ToString());
	    Timer.Reset();
	    Timer.Start();
            Console.WriteLine("\nGenerating sequence tags...");
            Raws.GenerateSequenceTags(WriteDeNovoTags);
	    Timer.Stop();
	    Duration = Timer.Elapsed;
	    Console.WriteLine("\tTime for de novo inference: {0}",Duration.ToString());
	    Timer.Reset();
	    Timer.Start();
            Raws.ComputeDistributions();
            Console.WriteLine("\nWriting TDAuditor-byRun and TDAuditor-byMSn TSV reports...");
            Raws.WriteTextQCReport();
	    Timer.Stop();
	    Duration = Timer.Elapsed;
	    Console.WriteLine("\tTime for computing quartiles and reporting: {0}",Duration.ToString());
        }

        static string SniffMSAlignForSource(string PathAndFile)
        {
            using (var msAlign = new StreamReader(PathAndFile))
            {
                var LineBuffer = msAlign.ReadLine();
                while (LineBuffer != null)
                {
                    if (LineBuffer.StartsWith("FILE_NAME="))
		    {
			var PathFile = LineBuffer.Substring(10, LineBuffer.Length - 15);
                        return Path.GetFileNameWithoutExtension(PathFile);
		    }
                    LineBuffer = msAlign.ReadLine();
                }
            }
            //FLASHDeconv does not include FILE_NAME attributes.
            //Just cut "_ms2" off this file name and pass it back.
            var ThisFile = Path.GetFileNameWithoutExtension(PathAndFile);
            return ThisFile.Substring(0, ThisFile.Length - 4);
        }
    }

    internal class MSMSPeak
    {
        public double Mass;
        public float Intensity;
        public int OrigZ;
        public int LongestTagStartingHere;
        public MSMSPeak Next;
        public MassGap AALinks;
    }

    class MassGap
    {
        public MSMSPeak NextPeak;
        public double   ExpectedMass;
	public int      AANumber;
        public MassGap  Next;

        public int DepthTagSearch()
        {
            if (NextPeak.AALinks == null)
            {
                return 1;
            }

            var MGRunner = NextPeak.AALinks;
            if (NextPeak.LongestTagStartingHere == 0)
            {
                var BestTagLength = 0;
                while (MGRunner != null)
                {
                    var ThisTagLength = MGRunner.DepthTagSearch();
                    if (ThisTagLength > BestTagLength) BestTagLength = ThisTagLength;
                    MGRunner = MGRunner.Next;
                }
                NextPeak.LongestTagStartingHere = BestTagLength + 1;
            }
            return NextPeak.LongestTagStartingHere;
        }
    }

    class SimilarityLink
    {
        public ScanMetrics Other;
        public double Score;
        public SimilarityLink Next;
    }

    class ScanMetrics
    {
        public string NativeID = "";
        public float ScanStartTime;
        public string mzMLDissociation = "";
        //TODO Should I record what dissociation type msAlign reports?
        public int mzMLPrecursorZ;
	public int mzMLMassResolvingPower = 0;
	public double mzMLSelectedIon = 0;
	public bool MatchedToDeconvolution = false;
        public int msAlignPrecursorZ;
        public double msAlignPrecursorMass = Double.NaN;
        public int mzMLPeakCount;
        public int msAlignPeakCount;
        public double[] PeakMZs;
        public int Degree;
        public int ComponentNumber;
        public bool Visited;
        public int AALinkCount;
        public int LongestTag;
        public ScanMetrics Next;
        public int ScanNumber;
        public SimilarityLink SimilarScans = new SimilarityLink();
        public SimilarityLink SimilarScansBeforeThis = new SimilarityLink();
        public static double FragmentTolerance = 0.02;
        private const double LowMZ = 400;
        private const double HighMZ = 2000;
        public static int NumberOfPossibleMasses = (int)Math.Ceiling((HighMZ - LowMZ) / FragmentTolerance);
        public static double[] LogFactorials;
        //TODO Allow users to specify alternative mass for Cys
        // Values from https://education.expasy.org/student_projects/isotopident/htdocs/aa-list.html
        public static double[] AminoAcids = {57.02146,71.03711,87.03203,97.05276,99.06841,101.04768,103.00919,113.08406,114.04293,
                          115.02694,128.05858,128.09496,129.04259,131.04049,137.05891,147.06841,156.10111,
                          163.06333,186.07931};
        public static string[] AminoAcidSymbols = { "G", "A", "S", "P", "V", "T", "C", "L/I", "N",
						    "D", "Q", "K", "E", "M", "H", "F", "R", "Y", "W" };

        public static void ComputeLogFactorials()
        {
            /*
              We will compute MS/MS match log probabilities by the
              hypergeometric distribution.  Therefore we will need log
              factorials; this will be our lookup table.
            */
            LogFactorials = new double[NumberOfPossibleMasses + 1];
            var Accum = 0.0;
            LogFactorials[0] = Math.Log(1);
            for (var index = 1; index <= NumberOfPossibleMasses; index++)
            {
                var ThisLog = Math.Log(index);
                Accum += ThisLog;
                //Console.WriteLine("index = {0}, ThisLog = {1}, Accum = {2}",index,ThisLog,Accum);
                LogFactorials[index] = Accum;
            }
        }

        public static double ComputeLogCombinationCount(int big, int little)
        {
            // This code computes numbers of combinations on a log scale
            var LogCombinationCount = LogFactorials[big] - (LogFactorials[little] + LogFactorials[big - little]);
            return LogCombinationCount;
        }

        public static double log1pexp(double x)
        {
            // This function prevents failures when probabilities are very close to zero.
            return x < Math.Log(Double.Epsilon) ? 0 : Math.Log(Math.Exp(x) + 1);
        }

        public static double sum_log_prob(double a, double b)
        {
            // This function adds together log probabilities efficiently.
            return a > b ? a + log1pexp(b - a) : b + log1pexp(a - b);
        }

        public double TestForSimilarity(ScanMetrics Other)
        {
            // Compare this MS/MS mass list to the other one; how many masses do the lists share?
            var ThisOffset = 0;
            var OtherOffset = 0;
            var MatchesSoFar = 0;
            while (ThisOffset < this.msAlignPeakCount && OtherOffset < Other.msAlignPeakCount)
            {
                var MassDiff = this.PeakMZs[ThisOffset] - Other.PeakMZs[OtherOffset];
                var AbsMassDiff = Math.Abs(MassDiff);
                if (AbsMassDiff < FragmentTolerance)
                {
                    MatchesSoFar++;
                    ThisOffset++;
                    OtherOffset++;
                }
                else
                {
                    if (MassDiff > 0)
                    {
                        OtherOffset++;
                    }
                    else
                    {
                        ThisOffset++;
                    }
                }
            }
            if (MatchesSoFar > 0)
            {
                //Compute point hypergeometric probability
                var CombA = this.msAlignPeakCount;
                var CombB = MatchesSoFar;
                var CombC = NumberOfPossibleMasses;
                var CombD = Other.msAlignPeakCount;
                var LPSum = ComputeLogCombinationCount(CombA, CombB) +
		    ComputeLogCombinationCount(CombC - CombA, CombD - CombB) -
		    ComputeLogCombinationCount(CombC, CombD);
                var MostMatchesPossible = Math.Min(this.msAlignPeakCount, Other.msAlignPeakCount);
                /*
                  At the moment, LPSum equals the log probability of
                  matching exactly this many peaks.  We will now add
                  the probabilities for cases with more peaks matching
                  than this.
                 */
		/*
                for (var MoreMatches = MatchesSoFar + 1; MoreMatches <= MostMatchesPossible; MoreMatches++)
                {
                    LPSum = sum_log_prob(LPSum, ComputeLogCombinationCount(CombA, MoreMatches) +
					 ComputeLogCombinationCount(CombC - CombA, CombD - MoreMatches) -
					 ComputeLogCombinationCount(CombC, CombD));
                }
		*/
                var NegLogProbability = -LPSum;
                return NegLogProbability;
            }

            return 0;
        }

	public void GenerateForwardSimilarityLinks(object obj)
	{
	    var OtherScan = this.Next;
	    var SimilarityRunner = this.SimilarScans;
	    while (OtherScan != null)
	    {
		if (OtherScan.msAlignPeakCount > 0)
		{
		    // Test these two scans to determine if they have an improbable amount of deconvolved mass overlap.
		    var ThisMatchScore = this.TestForSimilarity(OtherScan);
		    /*
		      100 is a very arbitrary threshold...
		      ThisMatchScore is a log probability that the
		      spectra would share this many overlapping peaks
		      by random chance.
		    */
		    if (ThisMatchScore > 100)
		    {
			//Make a link between these MS/MS scans to reflect their high mass list overlap.
			var SimBuffer = SimilarityRunner.Next;
			SimilarityRunner.Next = new SimilarityLink();
			SimilarityRunner = SimilarityRunner.Next;
			SimilarityRunner.Other = OtherScan;
			SimilarityRunner.Score = ThisMatchScore;
			SimilarityRunner.Next = SimBuffer;
		    }
		}
		OtherScan = OtherScan.Next;
	    }
	}

        public int ComponentRecurse(int ComponentLabel)
        {
            if (Visited)
            {
                return 0;
            }

            Visited = true;
            ComponentNumber = ComponentLabel;
            var SizeContribution = 1;
            var SLRunner = SimilarScans.Next;
            while (SLRunner != null)
            {
                SizeContribution += SLRunner.Other.ComponentRecurse(ComponentLabel);
                SLRunner = SLRunner.Next;
            }
	    SLRunner = SimilarScansBeforeThis.Next;
            while (SLRunner != null)
            {
                SizeContribution += SLRunner.Other.ComponentRecurse(ComponentLabel);
                SLRunner = SLRunner.Next;
            }
            return SizeContribution;
        }

	public void SequenceTagThisSpectrum(object obj)
	{
	    /*
	      First, convert the array of mass values into a linked list again.
	    */
	    var LongestTagSoFar = 0;
	    var PeakList = new MSMSPeak();
	    var PRunner1 = PeakList;
	    foreach (var ThisPeak in this.PeakMZs)
	    {
		PRunner1.Next = new MSMSPeak();
		PRunner1 = PRunner1.Next;
		PRunner1.Mass = ThisPeak;
	    }
	    /*
	      Find the gaps corresponding to amino acid masses.
	    */
	    PRunner1 = PeakList.Next;
	    MassGap MGBuffer;
	    while (PRunner1 != null)
	    {
		var PRunner2 = PRunner1.Next;
		while (PRunner2 != null)
		{
		    var MassDiff = PRunner2.Mass - PRunner1.Mass;
		    var index = 0;
		    foreach (var ThisAA in ScanMetrics.AminoAcids)
		    {
			var MassError = Math.Abs(MassDiff - ThisAA);
			if (MassError < ScanMetrics.FragmentTolerance)
			{
			    // ThisAA corresponds in mass to the separation between these two peaks
			    this.AALinkCount++;
			    MGBuffer = PRunner1.AALinks;
			    PRunner1.AALinks = new MassGap();
			    PRunner1.AALinks.Next = MGBuffer;
			    PRunner1.AALinks.NextPeak = PRunner2;
			    PRunner1.AALinks.ExpectedMass = ThisAA;
			    PRunner1.AALinks.AANumber = index;
			}
			index++;
		    }
		    PRunner2 = PRunner2.Next;
		}
		PRunner1 = PRunner1.Next;
	    }
	    /*
	      Use recursion to seek the longest sequence
	      for which each consecutive fragment exists.
	    */
	    PRunner1 = PeakList.Next;
	    while (PRunner1 != null)
	    {
		if (PRunner1.LongestTagStartingHere == 0)
		{
		    MGBuffer = PRunner1.AALinks;
		    while (MGBuffer != null)
		    {
			var ThisTagLength = MGBuffer.DepthTagSearch();
			if (ThisTagLength > LongestTagSoFar)
			{
			    LongestTagSoFar = ThisTagLength;
			}
			MGBuffer = MGBuffer.Next;
		    }
		}
		PRunner1 = PRunner1.Next;
	    }
	    this.LongestTag = LongestTagSoFar;
	}

	public void WriteDeNovoGraph(string Filename)
	{
	    /* It's a kludge to copy so much code from the above
	     * function for this, but I don't want multiple threads
	     * all trying to write their graphs to disk at once. */
	    var PeakList = new MSMSPeak();
	    var PRunner1 = PeakList;
	    foreach (var ThisPeak in this.PeakMZs)
	    {
		PRunner1.Next = new MSMSPeak();
		PRunner1 = PRunner1.Next;
		PRunner1.Mass = ThisPeak;
	    }
	    /*
	      Find the gaps corresponding to amino acid masses.
	    */
	    PRunner1 = PeakList.Next;
	    MassGap MGBuffer;
	    while (PRunner1 != null)
	    {
		var PRunner2 = PRunner1.Next;
		while (PRunner2 != null)
		{
		    var MassDiff = PRunner2.Mass - PRunner1.Mass;
		    var index = 0;
		    foreach (var ThisAA in ScanMetrics.AminoAcids)
		    {
			var MassError = Math.Abs(MassDiff - ThisAA);
			if (MassError < ScanMetrics.FragmentTolerance)
			{
			    // ThisAA corresponds in mass to the separation between these two peaks
			    MGBuffer = PRunner1.AALinks;
			    PRunner1.AALinks = new MassGap();
			    PRunner1.AALinks.Next = MGBuffer;
			    PRunner1.AALinks.NextPeak = PRunner2;
			    PRunner1.AALinks.ExpectedMass = ThisAA;
			    PRunner1.AALinks.AANumber = index;
			}
			index++;
		    }
		    PRunner2 = PRunner2.Next;
		}
		PRunner1 = PRunner1.Next;
	    }
	    StreamWriter DOTFile = new StreamWriter(Filename);
	    DOTFile.WriteLine("graph DeNovo {");
	    PRunner1 = PeakList.Next;
	    while (PRunner1 != null)
	    {
		MGBuffer = PRunner1.AALinks;
		while (MGBuffer != null)
		{
		    DOTFile.WriteLine(Math.Round(PRunner1.Mass,3) + "--" + Math.Round(MGBuffer.NextPeak.Mass,3) + " [label=\"" + ScanMetrics.AminoAcidSymbols[MGBuffer.AANumber] +"\"]");
		    MGBuffer = MGBuffer.Next;
		}
		PRunner1 = PRunner1.Next;
	    }
	    DOTFile.WriteLine("}");
	    DOTFile.Flush();
	}
    }

    class LCMSMSExperiment
    {
        // Fields read directly from file
        public string SourceFile = "";
        public string Instrument = "";
        public string SerialNumber = "";
        public string StartTimeStamp = "";
        public float MaxScanStartTime;
        // Computed fields
        public int mzMLMS1Count;
        public int mzMLMSnCount;
	public int MatchedToDeconvolution;
        public int msAlignMSnCount;
	// This next count includes only the MSn scans with zero peaks in their deconvolutions
        public int msAlignMSnCount0;
        /*
          The following metrics characterize the extent to which MSn
          scans in this experiment share fragment ions.
        */
        // What fraction of all possible MSn-MSn links were detected in deconvolved mass lists?
        public float Redundancy;
        // What is the largest number of scans found to match fragments with a single MSn?
        public int HighestDegree;
        // What is the largest set of MSn scans found to share fragments with each other?
        public int LargestComponentSize;
        // Which component number is the biggest one?
        public int LargestComponentIndex;
        // How many different components do these MSn separate into?
        public int ComponentCount;
        // The following MSn counts reflect mzML information
        public int mzMLHCDCount;
        public int mzMLCIDCount;
        public int mzMLETDCount;
        public int mzMLECDCount;
        public int mzMLEThcDCount;
        public int mzMLETciDCount;
        // Charge state histograms
        public static int MaxZ = 100;
        public static int MaxLength = 50;
        public static int MaxPkCount = 10000;
        public int[] mzMLPrecursorZDistn = new int[MaxZ + 1];
        public int[] mzMLPrecursorZQuartiles;
        public int[] msAlignPrecursorZDistn = new int[MaxZ + 1];
        public int[] msAlignPrecursorZQuartiles;
	public double[] msAlignPrecursorMassQuartiles = new double[5];
        public int[] mzMLPeakCountDistn = new int[MaxPkCount + 1];
        public int[] mzMLPeakCountQuartiles;
        public int[] msAlignPeakCountDistn = new int[MaxPkCount + 1];
        public int[] msAlignPeakCountQuartiles;
	public int   AALinkCountAbove2 = 0;
        public int[] AALinkCountDistn = new int[MaxPkCount + 1];
        public int[] AALinkCountQuartiles;
	public int   LongestTagAbove2 = 0;
        public int[] LongestTagDistn = new int[MaxLength + 1];
        public int[] LongestTagQuartiles;
        // Per-scan metrics
        public ScanMetrics ScansTable = new ScanMetrics();
        private ScanMetrics ScansRunner;
        public LCMSMSExperiment Next;
        private int LastPeakCount;
        private string LastNativeID = "";
        private bool ThisScanIsCID;
        private bool ThisScanIsHCD;
        private bool ThisScanIsECD;
        private bool ThisScanIsETD;
        private bool NeedToFinalizeActivation;

        public void ReadFromMZML(XmlReader Xread)
        {
            /*
              Do not think of this code as a general-purpose mzML
              reader.  It is intended to populate only the fields that
              TDAuditor cares about.  For example, it entirely ignores
              the array of m/z values and intensities stored for any
              spectra.  This is intended to glean only the required
              fields in a single pass of the file.  It uses only the
              System.Xml libraries from Microsoft, obviating the need
              for any add-in libraries (to simplify the build process
              to something even I can use).
             */
            ScansRunner = ScansTable;
            while (Xread.Read())
            {
                var ThisNodeType = Xread.NodeType;
                if (ThisNodeType == XmlNodeType.EndElement)
                {
                    if (Xread.Name == "spectrum" && NeedToFinalizeActivation)
                    {
                        /*
                          When we reach the end of an MS/MS spectrum,
                          we need to summarize its activation data to
                          a single string.  "EThcD" for example, means
                          that CV terms for both "ETD" and "beam-type
                          CID" were detected for a particular MS/MS.
                        */
                        // At present, only ETD can exist in combination with other activation types.
                        if (ThisScanIsETD)
                        {
                            if (ThisScanIsCID)
                            {
                                ScansRunner.mzMLDissociation = "ETciD";
                                mzMLETciDCount++;
                            }
                            else
                            if (ThisScanIsHCD)
                            {
                                ScansRunner.mzMLDissociation = "EThcD";
                                mzMLEThcDCount++;
                            }
                            else
                            {
                                ScansRunner.mzMLDissociation = "ETD";
                                mzMLETDCount++;
                            }
                        }
                        else
                        {
                            // I did not intend to write code that favors one activation over another, but this prioritizes CID and HCD over ECD.
                            if (ThisScanIsCID)
                            {
                                ScansRunner.mzMLDissociation = "CID";
                                mzMLCIDCount++;
                            }
                            else if (ThisScanIsHCD)
                            {
                                ScansRunner.mzMLDissociation = "HCD";
                                mzMLHCDCount++;
                            }
                            else if (ThisScanIsECD)
                            {
                                ScansRunner.mzMLDissociation = "ECD";
                                mzMLECDCount++;
                            }
                        }
                        NeedToFinalizeActivation = false;
                    }
                }
                else if (ThisNodeType == XmlNodeType.Element)
                {
                    if (Xread.Name == "run")
                    {
                        /*
                          We directly read relatively little
                          information about the mzML file as a whole.
                          Here we grab the startTimeStamp, and
                          elsewhere we grab the file name root,
                          instrument model, and serial number.
                        */
                        StartTimeStamp = Xread.GetAttribute("startTimeStamp");
                    }
                    else if (Xread.Name == "spectrum")
                    {
                        /*
                          We only create a new ScanMetrics object if
                          it isn't an MS1 scan.  We need to keep two
                          pieces of information from this new spectrum
                          header in case we do make a new ScanMetrics
                          object.
                        */
                        var ThisPeakCount = Xread.GetAttribute("defaultArrayLength");
                        LastPeakCount = int.Parse(ThisPeakCount);
                        LastNativeID = Xread.GetAttribute("id");
                    }
                    else if (Xread.Name == "cvParam")
                    {
                        var Accession = Xread.GetAttribute("accession");
                        switch (Accession)
                        {
                            /*
                              If you see that instrument model is ever
                              blank for an mzML, there are two likely
                              causes.  The first would be that the
                              mzML converter has not listed the CV
                              term for the instrument type in the
                              mzML-- ProteoWizard _does_ record this
                              information.  The second likely cause is
                              that the CV term relating to your
                              instrument model is missing from this
                              list.  Just add a "case" line for it and
                              recompile.
                             */
                            case "MS:1000557":
			    case "MS:1000932":
			    case "MS:1001742":
                            case "MS:1001910":
                            case "MS:1001911":
                            case "MS:1002416":
                            case "MS:1002523":
			    case "MS:1002533":
			    case "MS:1002634":
                            case "MS:1002732":
			    case "MS:1002877":
			    case "MS:1003005":
			    case "MS:1003028":
                            case "MS:1003029":
			    case "MS:1003094":
			    case "MS:1003123":
			    case "MS:1003293":
                                Instrument = Xread.GetAttribute("name");
                                break;
                            case "MS:1000529":
                                SerialNumber = Xread.GetAttribute("value");
                                break;
                            case "MS:1000016":
                                var ThisStartTime = Xread.GetAttribute("value");
                                // We need the "InvariantCulture" nonsense because some parts of the world separate decimals with commas.
                                var ThisStartTimeFloat = Single.Parse(ThisStartTime, CultureInfo.InvariantCulture);
                                ScansRunner.ScanStartTime = ThisStartTimeFloat;
                                if (ThisStartTimeFloat > MaxScanStartTime) MaxScanStartTime = ThisStartTimeFloat;
                                break;
                            case "MS:1000511":
                                var ThisLevel = Xread.GetAttribute("value");
                                var ThisLevelInt = int.Parse(ThisLevel);
                                if (ThisLevelInt == 1)
                                {
                                    // We do very little with MS scans other than count them.
                                    mzMLMS1Count++;
                                }
                                else
                                {
                                    /*
                                      If we detect an MS of level 2 or
                                      greater in the mzML, we have
                                      work to do.  Each MS/MS is
                                      matched by an item in the linked
                                      list of ScanMetrics for each
                                      LCMSMSExperiment.  We will need
                                      to capture some information we
                                      already saw (such as the
                                      NativeID of this scan) and set
                                      up for collection of some
                                      additional information in the
                                      Activation section.
                                     */
                                    mzMLMSnCount++;
                                    ScansRunner.Next = new ScanMetrics();
                                    ScansRunner = ScansRunner.Next;
                                    ScansRunner.NativeID = LastNativeID;
                                    ScansRunner.mzMLPeakCount = LastPeakCount;
                                    if (LastPeakCount > MaxPkCount)
                                        mzMLPeakCountDistn[MaxPkCount]++;
                                    else
                                        mzMLPeakCountDistn[LastPeakCount]++;
                                    ThisScanIsCID = false;
                                    ThisScanIsHCD = false;
                                    ThisScanIsECD = false;
                                    ThisScanIsETD = false;
                                    NeedToFinalizeActivation = true;
                                }
                                break;
                            case "MS:1000041":
                                var ThisCharge = Xread.GetAttribute("value");
                                var ThisChargeInt = int.Parse(ThisCharge);
                                ScansRunner.mzMLPrecursorZ = ThisChargeInt;
                                try
                                {
                                    mzMLPrecursorZDistn[ThisChargeInt]++;
                                }
                                catch (IndexOutOfRangeException)
                                {
                                    Console.Error.WriteLine("Maximum charge of {0} is less than mzML charge {1}.", MaxZ, ThisChargeInt);
                                }
                                break;
			    case "MS:1000744":
				var ThisMZ = Xread.GetAttribute("value");
				ScansRunner.mzMLSelectedIon = double.Parse(ThisMZ);
				break;
			    case "MS:1000800":
				var ThisPower = Xread.GetAttribute("value");
				ScansRunner.mzMLMassResolvingPower = int.Parse(ThisPower);
				break;
                            case "MS:1000133":
                                ThisScanIsCID = true;
                                break;
                            case "MS:1000422":
                                ThisScanIsHCD = true;
                                break;
                            case "MS:1000250":
                                ThisScanIsECD = true;
                                break;
                            case "MS:1000598":
                                ThisScanIsETD = true;
                                break;
                                /*  TODO: May need to implement IRMPD combinations, too.
                                case "MS:1000262":
                                ThisScanIsIRMPD = true;
                                break;
                                */

                        }
                    }
                }
            }
        }

        public LCMSMSExperiment Find(string Basename)
        {
            /*
              We receive an msAlign filename.  We seek the
              LCMSMSExperiment in this linked list that has the
              corresponding filename.
            */
            var LRunner = this.Next;
            while (LRunner != null)
            {
                if (Basename == LRunner.SourceFile)
                    return LRunner;
                LRunner = LRunner.Next;
            }
            return null;
        }

        public void ParseScanNumbers()
        {
            /*
              Instrument manufacturers differ in the ways that they
              report the identities of each MS and MS/MS.  Because
              TopFD was created for Thermo instruments, though, it
              expects each spectrum to have a unique scan number for a
              given RAW file.  To match to TopFD msAlign files, we'll
              need to extract those from the NativeIDs.
             */
            var SRunner = this.ScansTable.Next;
            while (SRunner != null)
            {
                // Example of Thermo NativeID: controllerType=0 controllerNumber=1 scan=12 (ProteoWizard)
                // Example of SCIEX NativeID: sample=1 period=1 cycle=806 experiment=2 (ProteoWizard)
		// Example of SCIEX NativeID: sample=1 period=1 cycle=7207 experiment=1 (SCIEX MS Data Converter)
                // Example of Bruker NativeID: scan=55 (TIMSConvert)
                // Example of Bruker NativeID: merged=102 frame=13 scanStart=810 scanEnd=834 (ProteoWizard)
                var Tokens = SRunner.NativeID.Split(' ');
		foreach (var ThisTerm in Tokens)
		{
		    var Tokens2 = ThisTerm.Split('=');
		    if ( Tokens2[0].Equals("cycle") || Tokens2[0].Equals("scan") || Tokens2[0].Equals("scanStart") )
		    {
			SRunner.ScanNumber = int.Parse(Tokens2[1]);
		    }
		}
                SRunner = SRunner.Next;
            }
        }

        public ScanMetrics GoToScan(int Target)
        {
            var SRunner = this.ScansTable.Next;
            while (SRunner != null)
            {
                if (SRunner.ScanNumber == Target)
                    return SRunner;
                SRunner = SRunner.Next;
            }
            return null;
        }
	
	public void ReadFromMGF(string PathAndFileName)
	{
	    using (var MGFFile = new StreamReader(PathAndFileName))
	    {
		var LineBuffer = MGFFile.ReadLine();
		var LastBasename = "";
		double LastMass=0;
		LCMSMSExperiment RawRunner = this;
		ScanMetrics ScanRunner = null;
		MSMSPeak PeakList = null;
		MSMSPeak PeakRunner = null;
		while (LineBuffer != null)
		{
                    string[] Tokens;
		    string[] SemiTokens;
                    if (LineBuffer.Contains("="))
                    {
                        Tokens = LineBuffer.Split('=');
                        int NumberFromString;
                        switch (Tokens[0])
                        {
                            case "TITLE":
				SemiTokens = Tokens[1].Split('\"');
				var Basename = Path.GetFileNameWithoutExtension(SemiTokens[1]);
				var Scan = SemiTokens[7];
				if (!LastBasename.Equals(Basename))
				{
				    Console.WriteLine("\t\tHandling spectra from {0}",Basename);
				    LastBasename = Basename;
				}
				//Now advance our runners to the corresponding scan in the corresponding RAW...
				RawRunner = this.Find(Basename);
				if (RawRunner == null)
				{
				    Console.Error.WriteLine("Error!  Could not find {0} among mzMLs.",Basename);
				}
				else
				{
				    try {
					NumberFromString = int.Parse(Scan);
					PeakList = new MSMSPeak();
					PeakRunner = PeakList;
					ScanRunner = RawRunner.GoToScan(NumberFromString);
					if (ScanRunner == null) Console.Error.WriteLine("Error!  Could not find scan {0}.",NumberFromString);
					else ScanRunner.MatchedToDeconvolution = true;
				    }
				    catch (FormatException) {
					Console.Error.WriteLine("Scan number could not be parsed from {0}", LineBuffer);
				    }
				}
				break;
			    case "PEPMASS":
				SemiTokens = Tokens[1].Split(' ');
				try {
				    LastMass = double.Parse(SemiTokens[0], CultureInfo.InvariantCulture);
				}
				catch (FormatException) {
				    Console.Error.WriteLine("Mass could not be parsed from {0}", LineBuffer);
				}
				break;
			    case "CHARGE":
				SemiTokens = Tokens[1].Split(new Char[] {'+','-'});
				try {
				    NumberFromString = int.Parse(SemiTokens[0]);
				    if ((RawRunner != null) && (ScanRunner != null))
				    {
					// We want to the final precursor charge for each scan to be the highest of multiple possibilities that Xtract puts forward.
					if (NumberFromString > ScanRunner.msAlignPrecursorZ)
					{
					    ScanRunner.msAlignPrecursorZ = NumberFromString;
					    ScanRunner.msAlignPrecursorMass = (LastMass-1.00727647)*NumberFromString;
					}
				    }
				}
				catch (FormatException) {
				    Console.Error.WriteLine("Charge could not be parsed from {0}", LineBuffer);
				}
				break;
			    default:
				break;
			}
		    }
		    else if (LineBuffer.Length > 0 && char.IsDigit(LineBuffer[0]))
		    {
			//This is a line containing a deconvolved mass, intensity, and original charge, delimited by whitespace
                        Tokens = LineBuffer.Split(null);
                        PeakRunner.Next = new MSMSPeak();
                        /*
                          This new linked list is temporary storage
                          while we're on this scan; we'll dump the
                          mass list to an array in a moment.
                        */
                        PeakRunner = PeakRunner.Next;
                        PeakRunner.Mass = double.Parse(Tokens[0], CultureInfo.InvariantCulture);
                        PeakRunner.Intensity = float.Parse(Tokens[1], CultureInfo.InvariantCulture);
			//ProSightPD does not write fragment mass charges.
                        //PeakRunner.OrigZ = int.Parse(Tokens[2]);
		    }
                    else if (LineBuffer == "END IONS")
                    {
			if ((RawRunner != null) && (ScanRunner != null))
			{
			    if (ScanRunner.PeakMZs == null) {
				// Copy the linked list masses to an array and sort it.
				var PeakCount = 0;
				PeakRunner = PeakList.Next;
				while (PeakRunner != null)
				{
				    PeakCount++;
				    PeakRunner = PeakRunner.Next;
				}
				ScanRunner.PeakMZs = new double[PeakCount];
				var Offset = 0;
				PeakRunner = PeakList.Next;
				while (PeakRunner != null)
				{
				    ScanRunner.PeakMZs[Offset] = PeakRunner.Mass;
				    Offset++;
				    PeakRunner = PeakRunner.Next;
				}
				Array.Sort(ScanRunner.PeakMZs);
				ScanRunner.msAlignPeakCount=PeakCount;
			    }
			}
                    }
		    LineBuffer = MGFFile.ReadLine();
		}
	    }
	}

	public void UpdateAllmsAlignStats()
	{
	    //Now that we're done reading the deconvolutions, let's record our summary statistics.
	    var RawRunner = this.Next;
	    while (RawRunner != null)
	    {
		var ScanRunner = RawRunner.ScansTable.Next;
		while (ScanRunner != null)
		{
		    if (ScanRunner.MatchedToDeconvolution) {
			RawRunner.MatchedToDeconvolution++;
			//Update msAlignMSnCount
			RawRunner.msAlignMSnCount++;
			//Update msAlignMSnCount0
			if (ScanRunner.msAlignPeakCount == 0) RawRunner.msAlignMSnCount0++;
			//Update msAlignPrecursorZDistn
			try
			{
			    RawRunner.msAlignPrecursorZDistn[ScanRunner.msAlignPrecursorZ]++;
			}
			catch (IndexOutOfRangeException)
			{
			    Console.Error.WriteLine("Reported precursor charge of {0} is greater than ceiling of {1}.", ScanRunner.msAlignPrecursorZ, MaxZ);
			}
			//Update msAlignPeakCountDistn
			if (ScanRunner.msAlignPeakCount > MaxPkCount)
			{
			    RawRunner.msAlignPeakCountDistn[MaxPkCount]++;
			}
			else
			{
			    RawRunner.msAlignPeakCountDistn[ScanRunner.msAlignPeakCount]++;
			}
		    }
		    ScanRunner = ScanRunner.Next;
		}
		RawRunner = RawRunner.Next;
	    }	    
	}
	
        public void ReadFromMSAlign(string PathAndFileName)
        {
            /*
              An earlier function to read mzML files is handed a
              particular XML Reader object that has already been
              initialized from a particular mzML file.  The MSAlign
              reader is simpler and more complex for a variety of
              reasons.  1) Since it looks a lot like MGF, it's an easy
              text format to parse.  2) Since in some cases multiple
              msAligns can be produced by topFD for an individual
              mzML, we may have some added characters in file names,
              making it tricky to match which mzML corresponds to each
              msAlign; this is handled by the Find function.  3) We
              have already imported information for mzMLs, and those
              data are all in RAM; now we need to add information from
              each msAlign to the correct LC-MS/MS data structure.
             */
            using (var msAlign = new StreamReader(PathAndFileName))
            {
                var LineBuffer = msAlign.ReadLine();
                ScanMetrics ScanRunner = null;
                MSMSPeak PeakList = null;
                MSMSPeak PeakRunner = null;
                while (LineBuffer != null)
                {
                    /*
                      We are particularly interested in the block of
                      lines at the start of each spectrum that
                      contains a variable and a value, separated by an
                      equals symbol.
                    */
                    string[] Tokens;
		    char[] Delimiters = {'=', ':'};
                    if (LineBuffer.Contains("="))
                    {
                        Tokens = LineBuffer.Split(Delimiters);
                        int NumberFromString;
			if (Tokens[1].Length > 0) {
			    switch (Tokens[0])
			    {
				case "SCANS":
				    try {
					NumberFromString = int.Parse(Tokens[1]);
					ScanRunner = this.GoToScan(NumberFromString);
					if (ScanRunner == null)
					{
					    Console.Error.WriteLine("Error seeking scan {0} from {1}", NumberFromString, PathAndFileName);
					}
					else ScanRunner.MatchedToDeconvolution=true;
				    }
				    catch (FormatException) {
					Console.Error.WriteLine("This SCANS number could not be parsed: {0}", LineBuffer);
				    }
				    PeakList = new MSMSPeak();
				    PeakRunner = PeakList;
				    break;
				case "PRECURSOR_CHARGE":
				    NumberFromString = int.Parse(Tokens[1]);
				    ScanRunner.msAlignPrecursorZ = NumberFromString;
				    break;
				case "PRECURSOR_MASS":
				    ScanRunner.msAlignPrecursorMass = double.Parse(Tokens[1], CultureInfo.InvariantCulture);
				    break;
			    }
			}
                    }
                    else if (LineBuffer.Length > 0 && char.IsDigit(LineBuffer[0]))
                    {
                        //This is a line containing a deconvolved mass, intensity, and original charge, delimited by whitespace
                        Tokens = LineBuffer.Split(null);
                        PeakRunner.Next = new MSMSPeak();
                        /*
                          This new linked list is temporary storage
                          while we're on this scan; we'll dump the
                          mass list to an array in a moment.
                        */
                        PeakRunner = PeakRunner.Next;
                        PeakRunner.Mass = double.Parse(Tokens[0], CultureInfo.InvariantCulture);
                        PeakRunner.Intensity = float.Parse(Tokens[1], CultureInfo.InvariantCulture);
                        PeakRunner.OrigZ = int.Parse(Tokens[2]);
                        ScanRunner.msAlignPeakCount++;
                    }
                    else if (LineBuffer == "END IONS")
                    {
                        if (ScanRunner.msAlignPeakCount > 0)
                        {
                            // Copy the linked list masses to an array and sort it.
                            ScanRunner.PeakMZs = new double[ScanRunner.msAlignPeakCount];
                            var Offset = 0;
                            PeakRunner = PeakList.Next;
                            while (PeakRunner != null)
                            {
                                ScanRunner.PeakMZs[Offset] = PeakRunner.Mass;
                                Offset++;
                                PeakRunner = PeakRunner.Next;
                            }
                            Array.Sort(ScanRunner.PeakMZs);
                        }
                    }
                    LineBuffer = msAlign.ReadLine();
                }
            }
        }

        public void FindSimilarSpectraWithinRaw(bool WriteConnectedComponents)
        {
            var LCMSMSRunner = this.Next;
            ScanMetrics.ComputeLogFactorials();
            while (LCMSMSRunner != null)
            {
                var SMRunner = LCMSMSRunner.ScansTable.Next;
                var NonVacantScanCount = 0;
                var LinkCount = 0;
                while (SMRunner != null)
                {
                    if (SMRunner.msAlignPeakCount > 0)
                    {
                        // NonVacantScanCount should, in the end, be the same as msAlignMSnCount - msAlignMSnCount0
                        NonVacantScanCount++;
			//SMRunner.GenerateForwardSimilarityLinks();
			ThreadPool.QueueUserWorkItem(new WaitCallback(SMRunner.GenerateForwardSimilarityLinks));
		    }
                    SMRunner = SMRunner.Next;
                }
		// Here we need to wait until all those queued threads are complete
		var StillRunning = true;
		var junk = 0;
		var maxWorkerThreads = 0;
		var WorkerThreads = 0;
		ThreadPool.GetMaxThreads(out maxWorkerThreads, out junk);
		while (StillRunning)
		{
		    ThreadPool.GetAvailableThreads(out WorkerThreads, out junk);
		    //Console.WriteLine("Workers: {0}, Max: {1}",WorkerThreads, maxWorkerThreads);
		    if (WorkerThreads == maxWorkerThreads)
		    {
			StillRunning=false;
		    }
		    else
		    {
			Thread.Sleep(100);
		    }
		}
		// Having made our forward links in parallel, we now create the reverse links so we can find connected components.
		SMRunner = LCMSMSRunner.ScansTable.Next;
		while (SMRunner != null)
		{
		    var SimilarityRunner = SMRunner.SimilarScans.Next;
		    while (SimilarityRunner != null)
		    {
			var OtherScan = SimilarityRunner.Other;
			var OtherSimBuffer = OtherScan.SimilarScansBeforeThis.Next;
			OtherScan.SimilarScansBeforeThis.Next = new SimilarityLink();
			OtherScan.SimilarScansBeforeThis.Next.Next = OtherSimBuffer;
			OtherScan.SimilarScansBeforeThis.Next.Other = SMRunner;
			OtherScan.SimilarScansBeforeThis.Next.Score = SimilarityRunner.Score;
			SimilarityRunner = SimilarityRunner.Next;
			LinkCount++;
		    }
		    SMRunner = SMRunner.Next;
		}
                if (LinkCount == 0)
                {
                    LCMSMSRunner.Redundancy = 0;
                }
                else
                {
                    /*
                      The math here is based on the handshake problem:
                      in a party attended by N people, how many
                      distinct handshakes can take place?  The answer
                      is (N(N-1))/2.  If N==1, this would result in a
                      division by zero.
                    */
                    LCMSMSRunner.Redundancy = LinkCount / ((float)NonVacantScanCount * (NonVacantScanCount - 1) / 2.0f);
                }
                Console.WriteLine("\tDetected {0}% of possible MSn-MSn similarity in {1}",
                          Math.Round(LCMSMSRunner.Redundancy * 100, 2), LCMSMSRunner.SourceFile);

                SMRunner = LCMSMSRunner.ScansTable.Next;
                var MaxDegreeSoFar = 0;
                // First, ask how many spectra each MSn was linked to by similar fragment masses.
                while (SMRunner != null)
                {
                    var SLRunner = SMRunner.SimilarScans.Next;
                    var ThisDegree = 0;
                    while (SLRunner != null)
                    {
                        ThisDegree++;
                        SLRunner = SLRunner.Next;
                    }
		    SLRunner = SMRunner.SimilarScansBeforeThis.Next;
                    while (SLRunner != null)
                    {
                        ThisDegree++;
                        SLRunner = SLRunner.Next;
                    }
                    SMRunner.Degree = ThisDegree;
                    if (ThisDegree > MaxDegreeSoFar) MaxDegreeSoFar = ThisDegree;
                    // If an MS/MS contains no masses, don't count it as a component.
                    if (SMRunner.msAlignPeakCount == 0) SMRunner.Visited = true;
                    SMRunner = SMRunner.Next;
                }
                LCMSMSRunner.HighestDegree = MaxDegreeSoFar;
                // Next, subdivide the MSn scans into connected components.
                SMRunner = LCMSMSRunner.ScansTable.Next;
                var ComponentCount = 0;
                while (SMRunner != null)
                {
                    if (!SMRunner.Visited)
                    {
                        //This scan is a starting point for an unexplored component.
                        ComponentCount++;
                        SMRunner.Visited = true;
                        SMRunner.ComponentNumber = ComponentCount;
                        var SLRunner = SMRunner.SimilarScans.Next;
                        var ComponentSize = 1;
                        while (SLRunner != null)
                        {
                            ComponentSize += SLRunner.Other.ComponentRecurse(ComponentCount);
                            SLRunner = SLRunner.Next;
                        }
                        if (ComponentSize > LCMSMSRunner.LargestComponentSize)
                        {
                            LCMSMSRunner.LargestComponentSize = ComponentSize;
                            LCMSMSRunner.LargestComponentIndex = ComponentCount;
                        }
                        // Console.WriteLine("RAW\t{0}\tLabel\t{1}\tSize\t{2}",LCMSMSRunner.SourceFile,ComponentCount,ComponentSize);
                    }
                    SMRunner = SMRunner.Next;
                }
                LCMSMSRunner.ComponentCount = ComponentCount;
                //Only produce the graphical output of the biggest component if the command line indicates that is desired.
		if (WriteConnectedComponents)
		{
		    LCMSMSRunner.GraphVizPrintComponent(LCMSMSRunner.LargestComponentIndex);
		}
                // Cleanup memory.
                SMRunner = LCMSMSRunner.ScansTable.Next;
                while (SMRunner != null)
                {
                    SMRunner.SimilarScans.Next = null;
                    SMRunner = SMRunner.Next;
                }
                //We're all done with this mzML / msAlign pair.  Go to the next one.
                LCMSMSRunner = LCMSMSRunner.Next;
            }
        }

        public void GraphVizPrintComponent(int TargetComponentNumber)
        {
            /*
              The TargetComponentNumber tells us a connected component
              of MS/MS scans in this RAW file.  Create an input file
              for GraphViz DOT that can be used to visualize the
              component.
             */
            SimilarityLink SLRunner;
            var SMRunner = this.ScansTable.Next;
            using (var DOTFile = new StreamWriter(SourceFile + "-ConnectedComponent.txt"))
            {
                DOTFile.WriteLine("graph LargestComponent {");
                while (SMRunner != null)
                {
                    if (SMRunner.ComponentNumber == TargetComponentNumber)
                    {
                        SLRunner = SMRunner.SimilarScans.Next;
                        while (SLRunner != null)
                        {
			    DOTFile.WriteLine(SMRunner.ScanNumber + "--" + SLRunner.Other.ScanNumber);
                            SLRunner = SLRunner.Next;
                        }
                    }
                    SMRunner = SMRunner.Next;
                }
                DOTFile.WriteLine("}");
            }
        }

        public void GenerateSequenceTags(bool WriteDeNovoTags)
        {
            /*
              What is the longest sequence we can "read" from the
              fragment masses?  We seek gaps between fragments that
              match amino acid masses.  Then we use recursion to
              determine the length of the longest possible tag
              stringing together those gaps.
             */
            var LCMSMSRunner = this.Next;
            // What is the mass of the largest amino acid?  Helpfully, AminoAcids is already sorted.
            var BiggestAAPlusTol = ScanMetrics.AminoAcids[ScanMetrics.AminoAcids.Length - 1] + ScanMetrics.FragmentTolerance;
            while (LCMSMSRunner != null)
            {
                var LongestTagForThisRAW = 0;
                var SMRunner = LCMSMSRunner.ScansTable.Next;
                while (SMRunner != null)
                {
                    if (SMRunner.msAlignPeakCount > 1)
                    {
			ThreadPool.QueueUserWorkItem(new WaitCallback(SMRunner.SequenceTagThisSpectrum));
		    }
		    SMRunner = SMRunner.Next;
		}
		// Here we need to wait until all those queued threads are complete
		var StillRunning = true;
		var junk = 0;
		var maxWorkerThreads = 0;
		var WorkerThreads = 0;
		ThreadPool.GetMaxThreads(out maxWorkerThreads, out junk);
		while (StillRunning)
		{
		    ThreadPool.GetAvailableThreads(out WorkerThreads, out junk);
		    if (WorkerThreads == maxWorkerThreads)
		    {
			StillRunning=false;
		    }
		    else
		    {
			Thread.Sleep(100);
		    }
		}
		// Now update the AALinkCounts and TagLengths for all spectra in this RAW.
		SMRunner = LCMSMSRunner.ScansTable.Next;
		while (SMRunner != null)
		{
		    if (SMRunner.msAlignPeakCount > 1)
		    {
			if (SMRunner.AALinkCount > LCMSMSExperiment.MaxPkCount)
                            LCMSMSRunner.AALinkCountDistn[MaxPkCount]++;
                        else
                            LCMSMSRunner.AALinkCountDistn[SMRunner.AALinkCount]++;
			if (SMRunner.AALinkCount > 2)
			    LCMSMSRunner.AALinkCountAbove2++;
			if (SMRunner.LongestTag > LCMSMSExperiment.MaxLength)
			    LCMSMSRunner.LongestTagDistn[MaxLength]++;
			else
			    LCMSMSRunner.LongestTagDistn[SMRunner.LongestTag]++;
		        if (SMRunner.LongestTag > LongestTagForThisRAW) LongestTagForThisRAW = SMRunner.LongestTag;
			if (SMRunner.LongestTag > 2) LCMSMSRunner.LongestTagAbove2++;
			if (WriteDeNovoTags)
			{
			    SMRunner.WriteDeNovoGraph(LCMSMSRunner.SourceFile + "-" + SMRunner.ScanNumber + "-DeNovo.txt");
			}
		    }
		    SMRunner = SMRunner.Next;
		}
                Console.WriteLine("\tInferred sequence tags as long as {0} AAs in {1}", LongestTagForThisRAW, LCMSMSRunner.SourceFile);
                LCMSMSRunner = LCMSMSRunner.Next;
            }
        }

        public int[] QuartilesOf(int[] Histogram)
        {
            var Quartiles = new int[5];
            var Sum = 0;
            int index;
            var AwaitingMin = true;
            for (index = 0; index < Histogram.Length; index++)
            {
                if (AwaitingMin && Histogram[index] > 0)
                {
                    AwaitingMin = false;
                    Quartiles[0] = index;
                }
                if (Histogram[index] > 0)
                    Quartiles[4] = index;
                Sum += Histogram[index];
            }
            var CountQ1 = Sum / 4;
            var CountQ2 = Sum / 2;
            var CountQ3 = CountQ1 + CountQ2;
            Sum = 0;
            for (index = 0; index < Histogram.Length; index++)
            {
                var ThisCount = Histogram[index];
                if (Sum < CountQ1 && CountQ1 <= Sum + ThisCount)
                    Quartiles[1] = index;
                if (Sum < CountQ2 && CountQ2 <= Sum + ThisCount)
                    Quartiles[2] = index;
                if (Sum < CountQ3 && CountQ3 <= Sum + ThisCount)
                    Quartiles[3] = index;
                Sum += ThisCount;
            }
            return Quartiles;
        }

        public void ComputeDistributions()
        {
            var LCMSMSRunner = this.Next;
	    // TODO: include only MatchedToDeconvolution scans.
            while (LCMSMSRunner != null)
            {
                LCMSMSRunner.mzMLPrecursorZQuartiles = QuartilesOf(LCMSMSRunner.mzMLPrecursorZDistn);
                LCMSMSRunner.msAlignPrecursorZQuartiles = QuartilesOf(LCMSMSRunner.msAlignPrecursorZDistn);
                LCMSMSRunner.mzMLPeakCountQuartiles = QuartilesOf(LCMSMSRunner.mzMLPeakCountDistn);
                LCMSMSRunner.msAlignPeakCountQuartiles = QuartilesOf(LCMSMSRunner.msAlignPeakCountDistn);
                LCMSMSRunner.AALinkCountQuartiles = QuartilesOf(LCMSMSRunner.AALinkCountDistn);
                LCMSMSRunner.LongestTagQuartiles = QuartilesOf(LCMSMSRunner.LongestTagDistn);
		// We need to build a sorted array from the precursor masses to get its quartiles.
		var ScanCount = 0;
		var ArrayIndex = 0;
		var SMRunner = LCMSMSRunner.ScansTable.Next;
		while (SMRunner != null) {
		    if (SMRunner.msAlignPrecursorMass > 0) ScanCount++;
		    SMRunner = SMRunner.Next;
		}
		if (ScanCount == 0)
		{
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[0]=Double.NaN;
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[1]=Double.NaN;
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[2]=Double.NaN;
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[3]=Double.NaN;
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[4]=Double.NaN;
		}
		else
		{
		    var msAlignPrecursorMasses = new double[ScanCount];
		    SMRunner = LCMSMSRunner.ScansTable.Next;
		    while (SMRunner != null) {
			if (SMRunner.msAlignPrecursorMass > 0)
			{
			    msAlignPrecursorMasses[ArrayIndex] = SMRunner.msAlignPrecursorMass;
			    ArrayIndex++;
			}
			SMRunner = SMRunner.Next;
		    }
		    Array.Sort(msAlignPrecursorMasses);
		    // Now we can extract the min, the max, and the inner quartiles
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[0] = msAlignPrecursorMasses[0];
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[1] = msAlignPrecursorMasses[ScanCount/4];
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[2] = msAlignPrecursorMasses[ScanCount/2];
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[3] = msAlignPrecursorMasses[ScanCount/4 + ScanCount/2];
		    LCMSMSRunner.msAlignPrecursorMassQuartiles[4] = msAlignPrecursorMasses[ScanCount-1];
		}
                LCMSMSRunner = LCMSMSRunner.Next;
            }
        }

        public void WriteTextQCReport()
        {
            /*
              We have two TSV outputs.  The "byRun" report contains a
              row for each LC-MS/MS (or mzML) in this directory.  The
              "byMSn" report contains a row for each MS/MS in each
              mzML in this directory.
             */
	    //TODO: Should I be reporting distribution of deconvolved precursor mass by RAW?
            var LCMSMSRunner = this.Next;
            const string delim = "\t";
            using (var TSVbyRun = new StreamWriter("TDAuditor-byRun.tsv"))
            {
                TSVbyRun.WriteLine("SourceFile\tInstrument\tSerialNumber\tStartTimeStamp\tRTDuration" +
				   "\tmzMLMS1Count\tmzMLMSnCount\tDeconvMSnWithPeaksCount\tDeconvMSnWithoutPeaksCount\tDeconvMSnWithPeaksFraction" +
				   "\tRedundancy\tHighestDegree\tLargestComponentSize\tComponentCount" +
				   "\tmzMLHCDCount\tmzMLCIDCount\tmzMLETDCount\tmzMLECDCount\tmzMLEThcDCount\tmzMLETciDCount" +
				   "\tmzMLPreZMin\tmzMLPreZQ1\tmzMLPreZQ2\tmzMLPreZQ3\tmzMLPreZMax" +
				   "\tDeconvPreZMin\tDeconvPreZQ1\tDeconvPreZQ2\tDeconvPreZQ3\tDeconvPreZMax" +
				   "\tDeconvPreMassMin\tDeconvPreMassQ1\tDeconvPreMassQ2\tDeconvPreMassQ3\tDeconvPreMassMax" +
				   "\tmzMLPeakCountMin\tmzMLPeakCountQ1\tmzMLPeakCountQ2\tmzMLPeakCountQ3\tmzMLPeakCountMax" +
				   "\tDeconvPeakCountMin\tDeconvPeakCountQ1\tDeconvPeakCountQ2\tDeconvPeakCountQ3\tDeconvPeakCountMax" +
				   "\tAALinkCountMin\tAALinkCountQ1\tAALinkCountQ2\tAALinkCountQ3\tAALinkCountMax" +
				   "\tTagLengthMin\tTagLengthQ1\tTagLengthQ2\tTagLengthQ3\tTagLengthMax\tAALinkCountAbove2\tTagLengthAbove2");
                while (LCMSMSRunner != null)
                {
                    //We need to distinguish between MS/MS that yield deconvolved mass lists and those that don't.
                    var MSnCountWithPeaks = LCMSMSRunner.msAlignMSnCount - LCMSMSRunner.msAlignMSnCount0;
                    //What fraction of all collected MS/MS scans yielded a non-empty peaklist in deconvolution?
                    var MSnWithPeaksFraction = MSnCountWithPeaks / (float)LCMSMSRunner.mzMLMSnCount;
                    // Actually write the metrics to the byRun file...
                    TSVbyRun.Write(LCMSMSRunner.SourceFile + delim);
                    TSVbyRun.Write(LCMSMSRunner.Instrument + delim);
                    TSVbyRun.Write(LCMSMSRunner.SerialNumber + delim);
                    TSVbyRun.Write(LCMSMSRunner.StartTimeStamp + delim);
                    TSVbyRun.Write(LCMSMSRunner.MaxScanStartTime + delim);
                    TSVbyRun.Write(LCMSMSRunner.mzMLMS1Count + delim);
                    TSVbyRun.Write(LCMSMSRunner.mzMLMSnCount + delim);
		    //TSVbyRun.Write(LCMSMSRunner.MatchedToDeconvolution + delim);
                    TSVbyRun.Write(MSnCountWithPeaks + delim);
                    TSVbyRun.Write(LCMSMSRunner.msAlignMSnCount0 + delim);
                    TSVbyRun.Write(MSnWithPeaksFraction + delim);
                    TSVbyRun.Write(LCMSMSRunner.Redundancy + delim);
                    TSVbyRun.Write(LCMSMSRunner.HighestDegree + delim);
                    TSVbyRun.Write(LCMSMSRunner.LargestComponentSize + delim);
                    TSVbyRun.Write(LCMSMSRunner.ComponentCount + delim);
                    TSVbyRun.Write(LCMSMSRunner.mzMLHCDCount + delim);
                    TSVbyRun.Write(LCMSMSRunner.mzMLCIDCount + delim);
                    TSVbyRun.Write(LCMSMSRunner.mzMLETDCount + delim);
                    TSVbyRun.Write(LCMSMSRunner.mzMLECDCount + delim);
                    TSVbyRun.Write(LCMSMSRunner.mzMLEThcDCount + delim);
                    TSVbyRun.Write(LCMSMSRunner.mzMLETciDCount + delim);
                    foreach (var ThisQuartile in LCMSMSRunner.mzMLPrecursorZQuartiles)
                        TSVbyRun.Write(ThisQuartile + delim);
                    foreach (var ThisQuartile in LCMSMSRunner.msAlignPrecursorZQuartiles)
                        TSVbyRun.Write(ThisQuartile + delim);
		    foreach (var ThisQuartile in LCMSMSRunner.msAlignPrecursorMassQuartiles)
			TSVbyRun.Write(ThisQuartile + delim);
                    foreach (var ThisQuartile in LCMSMSRunner.mzMLPeakCountQuartiles)
                        TSVbyRun.Write(ThisQuartile + delim);
                    foreach (var ThisQuartile in LCMSMSRunner.msAlignPeakCountQuartiles)
                        TSVbyRun.Write(ThisQuartile + delim);
                    foreach (var ThisQuartile in LCMSMSRunner.AALinkCountQuartiles)
                        TSVbyRun.Write(ThisQuartile + delim);
                    foreach (var ThisQuartile in LCMSMSRunner.LongestTagQuartiles)
                        TSVbyRun.Write(ThisQuartile + delim);
                    TSVbyRun.Write(LCMSMSRunner.AALinkCountAbove2 + delim);
                    TSVbyRun.WriteLine(LCMSMSRunner.LongestTagAbove2);
                    LCMSMSRunner = LCMSMSRunner.Next;
                }
            }
            LCMSMSRunner = this.Next;
            using (var TSVbyScan = new StreamWriter("TDAuditor-byMSn.tsv"))
            {
                TSVbyScan.WriteLine("SourceFile\tNativeID\tScanNumber\tScanStartTime\tmzMLDissociation\tMatchedToDeconvolution\tmzMLPreZ\tmzMLSelectedIon\tmzMLMassResolvingPower\tDeconvPreZ\tDeconvPreMass\tmzMLPeakCount\tDeconvPeakCount\tDegree\tComponentNumber\tAALinkCount\tLongestTag");
                while (LCMSMSRunner != null)
                {
                    var SMRunner = LCMSMSRunner.ScansTable.Next;
                    while (SMRunner != null)
                    {
                        TSVbyScan.Write(LCMSMSRunner.SourceFile + delim);
                        TSVbyScan.Write(SMRunner.NativeID + delim);
                        TSVbyScan.Write(SMRunner.ScanNumber + delim);
                        TSVbyScan.Write(SMRunner.ScanStartTime + delim);
                        TSVbyScan.Write(SMRunner.mzMLDissociation + delim);
			TSVbyScan.Write(SMRunner.MatchedToDeconvolution + delim);
                        TSVbyScan.Write(SMRunner.mzMLPrecursorZ + delim);
			TSVbyScan.Write(SMRunner.mzMLSelectedIon + delim);
			TSVbyScan.Write(SMRunner.mzMLMassResolvingPower + delim);
                        TSVbyScan.Write(SMRunner.msAlignPrecursorZ + delim);
                        TSVbyScan.Write(SMRunner.msAlignPrecursorMass + delim);
                        TSVbyScan.Write(SMRunner.mzMLPeakCount + delim);
                        TSVbyScan.Write(SMRunner.msAlignPeakCount + delim);
                        TSVbyScan.Write(SMRunner.Degree + delim);
                        TSVbyScan.Write(SMRunner.ComponentNumber + delim);
                        TSVbyScan.Write(SMRunner.AALinkCount + delim);
                        TSVbyScan.WriteLine(SMRunner.LongestTag);
                        SMRunner = SMRunner.Next;
                    }
                    LCMSMSRunner = LCMSMSRunner.Next;
                }
            }
        }
    }
}
