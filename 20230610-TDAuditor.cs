using System;
using System.IO;
using System.Data;
using System.Text;
using System.Xml;
using System.Globalization;

namespace TDAuditor {

    class Program {

	static void Main(string[] args)
	{
	    // Use periods to separate decimals
	    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
	    Console.WriteLine("TDAuditor: Quality metrics for top-down proteomes");
	    Console.WriteLine("David L. Tabb, for the Laboratory of Julia Chamot-Rooke, Institut Pasteur");
	    Console.WriteLine("alpha version 20230721");

	    /*
	      Would like to have the following:
	      -What is the max resolution seen for MS scans?
	      -What is the max resolution seen for MSn scans?
	      -What is the redundancy of precursor mass measurements?
	      -For each MS/MS, determine if another MS/MS in this msAlign matches its fragments
	    */
	    
	    string CWD = Directory.GetCurrentDirectory();
	    string mzMLPattern = "*.mzML";
	    string msAlignPattern = "*ms2.msalign";
	    string[] mzMLs = Directory.GetFiles(CWD, mzMLPattern);
	    string[] msAligns = Directory.GetFiles(CWD, msAlignPattern);
	    LCMSMSExperiment Raws = new LCMSMSExperiment();
	    LCMSMSExperiment RawsRunner = Raws;
	    string   Basename;

	    Console.WriteLine("\nImporting from mzML files...");
	    foreach (string current in mzMLs)
	    {
		Basename = Path.GetFileNameWithoutExtension(current);
		Console.WriteLine("\tReading mzML {0}",Basename);
		RawsRunner.Next = new LCMSMSExperiment();
		string    FileSpec = Path.Combine(CWD, current);
		XmlReader XMLfile = XmlReader.Create(FileSpec);
		RawsRunner = RawsRunner.Next;
		RawsRunner.SourceFile = Basename;
		RawsRunner.ReadFromMZML(XMLfile);
		RawsRunner.ParseScanNumbers();
	    }
	    // TODO: The following will run into a problem if some has created a conjoint ms2.msalign file in TopPIC
	    Console.WriteLine("\nImporting from msAlign files...");
	    foreach (string current in msAligns)
	    {
		Basename = Path.GetFileNameWithoutExtension(current);
		Console.WriteLine("\tReading msAlign {0}",Basename);
		string SourcemzML = SniffMSAlignForSource(current);
		LCMSMSExperiment CorrespondingRaw = Raws.Find(SourcemzML);
		if (CorrespondingRaw == null) {
		    Console.Error.WriteLine("\tWARNING: {0} could not be matched to an mzML title.",Basename);
		}
		else {
		    CorrespondingRaw.ReadFromMSAlign(current);
		}
	    }
	    /*
	      TODO: we should really check to see if any of the mzMLs
	      lack corresponding msAlign files.
	     */
	    Console.WriteLine("\nSeeking MS/MS scan pairs with many shared masses...");
	    Raws.FindSimilarSpectraWithinRaw();
	    Console.WriteLine("\nWriting TDAuditor-byRun and TDAuditor-byMSn TSV reports...");
	    Raws.WriteTextQCReport();
	}
	
	static string SniffMSAlignForSource(string PathAndFile)
	{
	    using (StreamReader msAlign = new StreamReader(PathAndFile))
	    {
		string LineBuffer = msAlign.ReadLine();
		while (LineBuffer != null) {
		    if (LineBuffer.StartsWith("FILE_NAME="))
			return LineBuffer.Substring(10,LineBuffer.Length-15);
		    LineBuffer=msAlign.ReadLine();
		}
	    }
	    Console.Error.WriteLine("Could not determine source mzML from {0}.",PathAndFile);
	    Environment.Exit(1);
	    return "";
	}
    }

    class MSMSPeak {
	public double   Mass=0;
	public float    Intensity=0;
	public int      OrigZ=0;
	public MSMSPeak Next=null;
    }

    class SimilarityLink
    {
	public ScanMetrics    Other=null;
	public double         Score=0;
	public SimilarityLink Next=null;
    }
    
    class ScanMetrics {
	public string NativeID="";
	public float  ScanStartTime=0;
	public string mzMLDissociation="";
	//TODO Should I record what dissociation type msAlign reports?
	public int    mzMLPrecursorZ=0;
	public int    msAlignPrecursorZ=0;
	public double msAlignPrecursorMass=0;
	public int    mzMLPeakCount=0;
	public int    msAlignPeakCount=0;
	public double[] PeakMZs;
	//TODO Incorporate DirecTag data
	public float  DirecTagScore=0;
	public ScanMetrics Next = null;
	public int    ScanNumber;
	public SimilarityLink SimilarScans = new SimilarityLink();
	public  static double FragmentTolerance = 0.1;
	private static double LowMZ = 400;
	private static double HighMZ = 2000;
	public  static int    NumberOfPossibleMasses = (int)(Math.Ceiling((HighMZ-LowMZ)/FragmentTolerance));
	public  static double[] LogFactorials;

	public static void ComputeLogFactorials()
	{
	    /*
	      We will compute MS/MS match log probabilities by the
	      hypergeometric distribution.  Therefore we will need log
	      factorials; this will be our lookup table.
	    */
	    LogFactorials = new double[NumberOfPossibleMasses+1];
	    double Accum = 0;
	    double ThisLog;
	    LogFactorials[0] = Math.Log(1);
	    for (int index = 1; index <= NumberOfPossibleMasses; index++)
	    {
		ThisLog = Math.Log(index);
		Accum += ThisLog;
		//Console.WriteLine("index = {0}, ThisLog = {1}, Accum = {2}",index,ThisLog,Accum);
		LogFactorials[index] = Accum;
	    }
	}

	public static double ComputeLogCombinationCount(int big, int little)
	{
	    // This code computes numbers of combinations on a log scale
	    double LogCombinationCount;
	    LogCombinationCount = LogFactorials[big]-(LogFactorials[little]+LogFactorials[big-little]);
	    return(LogCombinationCount);
	}
	
	public static double log1pexp(double x)
	{
	    // This function prevents failures when probabilities are very close to zero.
	    return x < Math.Log(Double.Epsilon) ? 0 : Math.Log(Math.Exp(x)+1);
	}

	public static double sum_log_prob(double a, double b)
	{
	    // This function adds together log probabilities efficiently.
	    return a>b ? a+log1pexp(b-a):  b+log1pexp(a-b);
	}
    
	public double TestForSimilarity (ScanMetrics Other)
	{
	    // Compare this MS/MS mass list to the other one; how many masses do the lists share?
	    int ThisOffset = 0;
	    int OtherOffset = 0;
	    double MassDiff;
	    double AbsMassDiff;
	    int MatchesSoFar = 0;
	    int CombA;
	    int CombB;
	    int CombC;
	    int CombD;
	    double NegLogProbability;
	    double LPSum;
	    while ((ThisOffset < this.msAlignPeakCount) && (OtherOffset < Other.msAlignPeakCount))
	    {
		MassDiff = this.PeakMZs[ThisOffset] - Other.PeakMZs[OtherOffset];
		AbsMassDiff = Math.Abs(MassDiff);
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
		CombA = this.msAlignPeakCount;
		CombB = MatchesSoFar;
		CombC = NumberOfPossibleMasses;
		CombD = Other.msAlignPeakCount;
		LPSum = (ComputeLogCombinationCount(CombA,CombB)+ComputeLogCombinationCount(CombC-CombA,CombD-CombB))-ComputeLogCombinationCount(CombC,CombD);
		int MostMatchesPossible = Math.Min(this.msAlignPeakCount, Other.msAlignPeakCount);
		for (int MoreMatches = MatchesSoFar +1; MoreMatches <= MostMatchesPossible; MoreMatches++)
		{
		    LPSum = sum_log_prob(LPSum, (ComputeLogCombinationCount(CombA,MoreMatches)+ComputeLogCombinationCount(CombC-CombA,CombD-MoreMatches))-ComputeLogCombinationCount(CombC,CombD));
		}
		NegLogProbability = -LPSum;
		bool MassMatch = Math.Abs(this.msAlignPrecursorMass - Other.msAlignPrecursorMass) < 2.1;
		/*
		if(NegLogProbability > 100)
		{
		    Console.WriteLine("MatchedPeaks\t{0}\tThisPeaks\t{1}\tOtherPeaks\t{2}\tNegLogProb\t{3}\tThisMass\t{4}\tOtherMass\t{5}\tMassMatch\t{6}",
				      MatchesSoFar,CombA,CombD,NegLogProbability,this.msAlignPrecursorMass,Other.msAlignPrecursorMass,MassMatch);
		}
		*/
		return NegLogProbability;
	    }
	    else
	    {
		return 0;
	    }
	}
    }

    class LCMSMSExperiment {
	// Fields read directly from file
	public string SourceFile="";
	public string Instrument="";
	public string SerialNumber="";
	public string StartTimeStamp="";
	public float  MaxScanStartTime=0;
	// Computed fields
	public int    mzMLMS1Count=0;
	public int    mzMLMSnCount=0;
	public int    msAlignMSnCount=0;
	// This next count includes only the MSn scans with zero peaks in their deconvolutions
	public int    msAlignMSnCount0=0;
	// What fraction of all possible MSn-MSn links were detected in deconvolved mass lists?
	public float  Redundancy=0;
	// The following MSn counts reflect mzML information
	public int    mzMLHCDCount=0;
	public int    mzMLCIDCount=0;
	public int    mzMLETDCount=0;
	public int    mzMLECDCount=0;
	public int    mzMLEThcDCount=0;
	public int    mzMLETciDCount=0;
	// Charge state histograms
	public static int MaxZ=100;
	public int[]  mzMLPrecursorZ    = new int[MaxZ+1];
	public int[]  msAlignPrecursorZ = new int[MaxZ+1];
	public int    mzMLPrecursorZMin=MaxZ;
	public int    mzMLPrecursorZMax=0;
	public int    msAlignPrecursorZMin=MaxZ;
	public int    msAlignPrecursorZMax=0;
	// Per-scan metrics
	public  ScanMetrics ScansTable = new ScanMetrics();
	private ScanMetrics ScansRunner;
	public  LCMSMSExperiment Next = null;
	private int    LastPeakCount = 0;
	private string LastNativeID = "";
	private bool   ThisScanIsCID = false;
	private bool   ThisScanIsHCD = false;
	private bool   ThisScanIsECD = false;
	private bool   ThisScanIsETD = false;
	private bool   NeedToFinalizeActivation = false;
	
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
	    ScansRunner=ScansTable;
	    while (Xread.Read())
	    {
		XmlNodeType ThisNodeType = Xread.NodeType;
		if (ThisNodeType == XmlNodeType.EndElement)
		{
		    if (Xread.Name=="spectrum" && NeedToFinalizeActivation)
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
		    if (Xread.Name=="run") {
			/*
			  We directly read relatively little
			  information about the mzML file as a whole.
			  Here we grab the startTimeStamp, and
			  elsewhere we grab the file name root,
			  instrument model, and serial number.
			*/
			StartTimeStamp = Xread.GetAttribute("startTimeStamp");
		    }
		    else if (Xread.Name=="spectrum") {
			/*
			  We only create a new ScanMetrics object if
			  it isn't an MS1 scan.  We need to keep two
			  pieces of information from this new spectrum
			  header in case we do make a new ScanMetrics
			  object.
			*/
			string ThisPeakCount = Xread.GetAttribute("defaultArrayLength");
			LastPeakCount = int.Parse(ThisPeakCount);
			LastNativeID = Xread.GetAttribute("id");
		    }
		    else if (Xread.Name=="cvParam") {
			string Accession = Xread.GetAttribute("accession");
			switch(Accession)
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
			    case "MS:1001910":
			    case "MS:1001911":
			    case "MS:1002416":
			    case "MS:1002523":
			    case "MS:1002732":
			    case "MS:1003029":
				Instrument = Xread.GetAttribute("name");
				break;
			    case "MS:1000529":
				SerialNumber = Xread.GetAttribute("value");
				break;
			    case "MS:1000016":
				string ThisStartTime = Xread.GetAttribute("value");
				// We need the "InvariantCulture" nonsense because some parts of the world separate decimals with commas.
				float  ThisStartTimeFloat = Single.Parse(ThisStartTime, CultureInfo.InvariantCulture);
				ScansRunner.ScanStartTime = ThisStartTimeFloat;
				if (ThisStartTimeFloat > MaxScanStartTime) MaxScanStartTime = ThisStartTimeFloat;
				break;
			    case "MS:1000511":
				string ThisLevel = Xread.GetAttribute("value");
				int    ThisLevelInt = int.Parse(ThisLevel);
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
				    ThisScanIsCID = false;
				    ThisScanIsHCD = false;
				    ThisScanIsECD = false;
				    ThisScanIsETD = false;
				    NeedToFinalizeActivation = true;
				}
				break;
			    case "MS:1000041":
				string ThisCharge = Xread.GetAttribute("value");
				int    ThisChargeInt = int.Parse(ThisCharge);
				ScansRunner.mzMLPrecursorZ = ThisChargeInt;
				if (ThisChargeInt < mzMLPrecursorZMin) mzMLPrecursorZMin = ThisChargeInt;
				if (ThisChargeInt > mzMLPrecursorZMax) mzMLPrecursorZMax = ThisChargeInt;
				try {
				    mzMLPrecursorZ[ThisChargeInt]++;
				}
				catch (IndexOutOfRangeException) {
				    Console.Error.WriteLine("Maximum charge of {0} is less than mzML charge {1}.",MaxZ,ThisChargeInt);
				}
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
	    LCMSMSExperiment LRunner = this.Next;
	    while (LRunner != null) {
		if (Basename == LRunner.SourceFile)
		    return LRunner;
		LRunner = LRunner.Next;
	    }
	    return null;
	}

	public void ParseScanNumbers() {
	    /*
	      Instrument manufacturers differ in the ways that they
	      report the identities of each MS and MS/MS.  Because
	      TopFD was created for Thermo instruments, though, it
	      expects each spectrum to have a unique scan number for a
	      given RAW file.  To match to TopFD msAlign files, we'll
	      need to extract those from the NativeIDs.
	     */
	    ScanMetrics SRunner = this.ScansTable.Next;
	    while (SRunner != null)
	    {
		// Example of Thermo NativeID: controllerType=0 controllerNumber=1 scan=12
		string[] Tokens = SRunner.NativeID.Split(' ');
		string[] Tokens2 = Tokens[2].Split('=');
		SRunner.ScanNumber = int.Parse(Tokens2[1]);
		SRunner = SRunner.Next;
	    }
	}
	
	public ScanMetrics GoToScan(int Target) {
	    ScanMetrics SRunner = this.ScansTable.Next;
	    while (SRunner != null)
	    {
		if (SRunner.ScanNumber == Target)
		    return SRunner;
		SRunner = SRunner.Next;
	    }
	    return null;
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
	    using (StreamReader msAlign = new StreamReader(PathAndFileName))
	    {
		string      LineBuffer = msAlign.ReadLine();
		string []   Tokens;
		ScanMetrics ScanRunner = null;
		MSMSPeak    PeakList = null;
		MSMSPeak    PeakRunner = null;
		int         NumberFromString;
		while (LineBuffer != null) {
		    /*
		      We are particularly interested in the block of
		      lines at the start of each spectrum that
		      contains a variable and a value, separated by an
		      equals symbol.
		    */
		    if (LineBuffer.Contains("="))
		    {
			Tokens = LineBuffer.Split('=');
			switch (Tokens[0])
			{
			    case "SCANS":
				NumberFromString = int.Parse(Tokens[1]);
				ScanRunner = this.GoToScan(NumberFromString);
				if (ScanRunner == null) {
				    Console.Error.WriteLine("Error seeking scan {0} from {1}",NumberFromString,PathAndFileName);
				}
				PeakList = new MSMSPeak();
				PeakRunner = PeakList;
				this.msAlignMSnCount++;
				break;
			    case "PRECURSOR_CHARGE":
				NumberFromString = int.Parse(Tokens[1]);
				ScanRunner.msAlignPrecursorZ = NumberFromString;
				if (NumberFromString < this.msAlignPrecursorZMin) this.msAlignPrecursorZMin = NumberFromString;
				if (NumberFromString > this.msAlignPrecursorZMax) this.msAlignPrecursorZMax = NumberFromString;
				try {
				    this.msAlignPrecursorZ[NumberFromString]++;
				}
				catch (IndexOutOfRangeException) {
				    Console.Error.WriteLine("Maximum charge of {0} is less than msAlign charge {1}.",MaxZ,NumberFromString);
				}
				break;
			    case "PRECURSOR_MASS":
				ScanRunner.msAlignPrecursorMass = double.Parse(Tokens[1], CultureInfo.InvariantCulture);
				break;
			}
		    }
		    else if ((LineBuffer.Length > 0) && char.IsDigit(LineBuffer[0]))
		    {
			//This is a line containing a deconvolved mass, intensity, and original charge, delimited by whitespace
			Tokens = LineBuffer.Split(null);
			PeakRunner.Next = new MSMSPeak();
			//This new linked list is temporary storage while we're on this scan; we'll dump the mass list to an array in a moment.
			PeakRunner = PeakRunner.Next;
			PeakRunner.Mass = double.Parse(Tokens[0], CultureInfo.InvariantCulture);
			PeakRunner.Intensity = float.Parse(Tokens[1], CultureInfo.InvariantCulture);
			PeakRunner.OrigZ = int.Parse(Tokens[2]);
			ScanRunner.msAlignPeakCount++;
		    }
		    else if (LineBuffer == "END IONS")
		    {
			if (ScanRunner.msAlignPeakCount == 0) this.msAlignMSnCount0++;
			else
			{
			    // Copy the linked list masses to an array and sort it.
			    ScanRunner.PeakMZs = new double[ScanRunner.msAlignPeakCount];
			    int Offset = 0;
			    PeakRunner = PeakList.Next;
			    while (PeakRunner != null)
			    {
				ScanRunner.PeakMZs[Offset] = PeakRunner.Mass;
				Offset++;
				PeakRunner=PeakRunner.Next;
			    }
			    Array.Sort(ScanRunner.PeakMZs);
			}
		    }
		    LineBuffer = msAlign.ReadLine();
		}
	    }
	}

	public void FindSimilarSpectraWithinRaw()
	{
	    LCMSMSExperiment LCMSMSRunner = this.Next;
	    SimilarityLink   SimilarityRunner;
	    SimilarityLink   SimBuffer;
	    double           ThisMatchScore;
	    int              NonVacantScanCount;
	    int              LinkCount;
	    int              LinksPerMSn;
	    int              MostLinksPerMSn;
	    ScanMetrics.ComputeLogFactorials();
	    while (LCMSMSRunner != null) {
		ScanMetrics SMRunner = LCMSMSRunner.ScansTable.Next;
		NonVacantScanCount=0;
		LinkCount=0;
		MostLinksPerMSn=0;
		while (SMRunner != null) {
		    if (SMRunner.msAlignPeakCount > 0)
		    {
			ScanMetrics OtherScan = SMRunner.Next;
			SimilarityRunner = SMRunner.SimilarScans;
			//NonVacantScanCount should, in the end, be the same as msAlignMSnCount - msAlignMSnCount0
			NonVacantScanCount++;
			LinksPerMSn = 0;
			while (OtherScan != null)
			{
			    if (OtherScan.msAlignPeakCount > 0)
			    {
				//Test these two scans to determine if they have an improbable amount of deconvolved mass overlap.
				ThisMatchScore = SMRunner.TestForSimilarity(OtherScan);
				if (ThisMatchScore > 100)
				{
				    //Make a link between these MS/MS scans to reflect their high mass list overlap
				    SimBuffer=SimilarityRunner.Next;
				    SimilarityRunner.Next = new SimilarityLink();
				    SimilarityRunner = SimilarityRunner.Next;
				    SimilarityRunner.Other = OtherScan;
				    SimilarityRunner.Score = ThisMatchScore;
				    SimilarityRunner.Next = SimBuffer;
				    //Make the reverse link
				    SimBuffer = OtherScan.SimilarScans.Next;
				    OtherScan.SimilarScans.Next = new SimilarityLink();
				    OtherScan.SimilarScans.Next.Other=SMRunner;
				    OtherScan.SimilarScans.Next.Score = ThisMatchScore;
				    OtherScan.SimilarScans.Next.Next = SimBuffer;
				    LinkCount++;
				    LinksPerMSn++;
				}
			    }
			    OtherScan = OtherScan.Next;
			}
			if (LinksPerMSn > MostLinksPerMSn) MostLinksPerMSn = LinksPerMSn;
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
		    LCMSMSRunner.Redundancy = (float)LinkCount / (float)(NonVacantScanCount * (NonVacantScanCount-1) / 2);
		}
		Console.WriteLine("\tDetected {0} MSn-MSn links within {1}",
				  LinkCount,LCMSMSRunner.SourceFile);
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
	    LCMSMSExperiment LCMSMSRunner = this.Next;
	    string delim = "\t";
	    using (StreamWriter TSVbyRun = new StreamWriter("TDAuditor-byRun.tsv"))
	    {
		TSVbyRun.Write("SourceFile\tInstrument\tSerialNumber\tStartTimeStamp\tRTDuration\tMS1Count\tmzMLMSnCount\tmsAlignMSnCount\tmsAlignMSnCount0\tRedundancy\tmzMLHCDCount\tmzMLCIDCount\tmzMLETDCount\tmzMLECDCount\tmzMLEThcDCount\tmzMLETciDCount\tmzMLPreZMin\tmzMLPreZMax\tmsAlignPreZMin\tmsAlignPreZMax\tblank\t");
		for (int i=0; i<=MaxZ; i++)
		{
		    TSVbyRun.Write("mzMLPreZ={0}\t",i);
		}
		TSVbyRun.Write("blank\t");
		for (int i=0; i<=MaxZ; i++)
		{
		    TSVbyRun.Write("msAlignPreZ={0}\t",i);
		}
		TSVbyRun.WriteLine();
		while (LCMSMSRunner != null)
		{
		    TSVbyRun.Write(LCMSMSRunner.SourceFile + delim);
		    TSVbyRun.Write(LCMSMSRunner.Instrument + delim);
		    TSVbyRun.Write(LCMSMSRunner.SerialNumber + delim);
		    TSVbyRun.Write(LCMSMSRunner.StartTimeStamp + delim);
		    TSVbyRun.Write(LCMSMSRunner.MaxScanStartTime + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLMS1Count + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLMSnCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.msAlignMSnCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.msAlignMSnCount0 + delim);
		    TSVbyRun.Write(LCMSMSRunner.Redundancy + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLHCDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLCIDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLETDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLECDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLEThcDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLETciDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLPrecursorZMin + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLPrecursorZMax + delim);
		    TSVbyRun.Write(LCMSMSRunner.msAlignPrecursorZMin + delim);
		    TSVbyRun.Write(LCMSMSRunner.msAlignPrecursorZMax + delim);
		    TSVbyRun.Write(delim);
		    foreach(int charge in LCMSMSRunner.mzMLPrecursorZ)
		    {
			TSVbyRun.Write(charge + delim);
		    }
		    TSVbyRun.Write(delim);
		    foreach(int charge in LCMSMSRunner.msAlignPrecursorZ)
		    {
			TSVbyRun.Write(charge + delim);
		    }
		    TSVbyRun.WriteLine();
		    LCMSMSRunner = LCMSMSRunner.Next;
		}
	    }
	    LCMSMSRunner = this.Next;
	    using (StreamWriter TSVbyScan = new StreamWriter("TDAuditor-byMSn.tsv"))
	    {
		TSVbyScan.WriteLine("SourceFile\tNativeID\tScanStartTime\tmzMLDissociation\tmzMLPrecursorZ\tmsAlignPrecursorZ\tmsAlignPrecursorMass\tmzMLPeakCount\tmsAlignPeakCount");
		while (LCMSMSRunner != null) {
		    ScanMetrics SMRunner = LCMSMSRunner.ScansTable.Next;
		    while (SMRunner != null) {
			TSVbyScan.Write(LCMSMSRunner.SourceFile + delim);
			TSVbyScan.Write(SMRunner.NativeID + delim);
			TSVbyScan.Write(SMRunner.ScanStartTime + delim);
			TSVbyScan.Write(SMRunner.mzMLDissociation + delim);
			TSVbyScan.Write(SMRunner.mzMLPrecursorZ + delim);
			TSVbyScan.Write(SMRunner.msAlignPrecursorZ + delim);
			TSVbyScan.Write(SMRunner.msAlignPrecursorMass + delim);
			TSVbyScan.Write(SMRunner.mzMLPeakCount + delim);
			TSVbyScan.Write(SMRunner.msAlignPeakCount + delim);
			TSVbyScan.WriteLine();
			SMRunner = SMRunner.Next;
		    }
		    LCMSMSRunner = LCMSMSRunner.Next;
		}
	    }
	}
    }
}
