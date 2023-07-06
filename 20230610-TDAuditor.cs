using System;
using System.IO;
using System.Data;
using System.Text;
using System.Xml;

namespace TDAuditor {

    class Program {

	static void Main(string[] args)
	{
	    Console.WriteLine("TDAuditor: Quality metrics for top-down proteomes");
	    Console.WriteLine("David L. Tabb, for the Laboratory of Julia Chamot-Rooke, Institut Pasteur");
	    Console.WriteLine("alpha version 20230706");

	    /*
	      Would like to have the following:
	      -What is the max resolution seen for MS scans?
	      -What is the max resolution seen for MSn scans?
	      -What is the redundancy of precursor mass measurements?
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
	      At this point, we should really check to see if any of
	      the mzMLs lack corresponding msAlign files.
	     */
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
    
    class ScanMetrics {
	public string NativeID="";
	public float  ScanStartTime=0;
	public string mzMLDissociation="";
	//TODO Should I record what dissociation type msAlign reports?
	public int    mzMLPrecursorZ=0;
	public int    msAlignPrecursorZ=0;
	public int    mzMLPeakCount=0;
	public int    msAlignPeakCount=0;
	//TODO Incorporate DirecTag data
	public float  DirecTagScore=0;
	public ScanMetrics Next = null;
	public int    ScanNumber;
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
				float  ThisStartTimeFloat = float.Parse(ThisStartTime);
				ScansRunner.ScanStartTime = ThisStartTimeFloat;
				if (ThisStartTimeFloat > MaxScanStartTime) MaxScanStartTime = ThisStartTimeFloat;
				break;
			    case "MS:1000511":
				string ThisLevel = Xread.GetAttribute("value");
				int    ThisLevelInt = int.Parse(ThisLevel);
				if (ThisLevelInt == 1)
				{
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
				catch (IndexOutOfRangeException e) {
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
		string LineBuffer = msAlign.ReadLine();
		string [] Tokens;
		ScanMetrics ScanRunner = null;
		int NumberFromString;
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
				    catch (IndexOutOfRangeException e) {
					Console.Error.WriteLine("Maximum charge of {0} is less than msAlign charge {1}.",MaxZ,NumberFromString);
				    }
				    break;
				case "PRECURSOR_INTENSITY":
				    // We are interested in this one because it is the last before the mass list!
				    // TODO: Make this count the consecutive lines starting with a numeric value appearing before END IONs-- the current strategy will break with minor format variations.
				    int PkCount = 0;
				    LineBuffer = msAlign.ReadLine();
				    while (LineBuffer != "END IONS") {
					PkCount++;
					LineBuffer = msAlign.ReadLine();
				    }
				    ScanRunner.msAlignPeakCount = PkCount;
				    if (PkCount == 0) this.msAlignMSnCount0++;
				    break;
			    }
			}
		    LineBuffer = msAlign.ReadLine();
		}
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
		TSVbyRun.Write("SourceFile\tInstrument\tSerialNumber\tStartTimeStamp\tRTDuration\tMS1Count\tmzMLMSnCount\tmsAlignMSnCount\tmsAlignMSnCount0\tmzMLHCDCount\tmzMLCIDCount\tmzMLETDCount\tmzMLECDCount\tmzMLEThcDCount\tmzMLETciDCount\tmzMLPreZMin\tmzMLPreZMax\tmsAlignPreZMin\tmsAlignPreZMax\tblank\t");
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
		TSVbyScan.WriteLine("SourceFile\tNativeID\tScanStartTime\tmzMLDissociation\tmzMLPrecursorZ\tmsAlignPrecursorZ\tmzMLPeakCount\tmsAlignPeakCount");
		while (LCMSMSRunner != null) {
		    ScanMetrics SMRunner = LCMSMSRunner.ScansTable.Next;
		    while (SMRunner != null) {
			TSVbyScan.Write(LCMSMSRunner.SourceFile + delim);
			TSVbyScan.Write(SMRunner.NativeID + delim);
			TSVbyScan.Write(SMRunner.ScanStartTime + delim);
			TSVbyScan.Write(SMRunner.mzMLDissociation + delim);
			TSVbyScan.Write(SMRunner.mzMLPrecursorZ + delim);
			TSVbyScan.Write(SMRunner.msAlignPrecursorZ + delim);
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
