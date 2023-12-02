# TDAuditor: Assessing LC-MS/MS quality for top-down proteomics
The analytical chemistry and bioinformatics to identify intact proteoforms has developed quite substantially over the last decade.  The complexity of LC-MS/MS experiments and identification algorithms for top-down proteomics can pose a particular challenge when an experiment does not identify the proteoforms that were expected.  Was no tandem mass spectrum acquired for that precursor ion?  Did deconvolution successfully infer its charge state?  Were the fragment masses resulting from its dissociation viable for identification?  TDAuditor is intended to assist top-down proteome researchers in characterizing the LC-MS/MS data that they have acquired, appraising the impact of deconvolution on a scan-by-scan basis.

Practically speaking, TDAuditor collates data from mzML files (often created by ProteoWizard msConvert) and their corresponding msAlign files (typically created by TopFD in the TopPIC Suite or by FLASHDeconv from OpenMS 3.0).  The software is implemented in C#, a .NET language, and can be run in Microsoft Windows.  It creates reports in tab-separated values text files and exports visualizations in GraphViz .DOT text files.  The reports include TDAuditor-byRun.tsv, where each row represents an input mzML/msAlign pair and each column supplies a particular metric for that LC-MS/MS experiment, and TDAuditor-byMSn.tsv, where each row represents an MS/MS scan from the input data and metrics characterize these individual scans.

# TDAuditor-byRun.tsv Report for each LC-MS/MS
The metrics reported for each LC-MS/MS begin with some essential details:
* SourceFile: File Name
* Instrument: Instrument model
* SerialNumber: Instrument serial number (Thermo only)
* StartTimeStamp: Time at which LC-MS/MS began
* RTDuration: Highest retention time recorded in LC-MS/MS experiment

It continues with scan counts:
* mzMLMS1Count: Number of MS1 scans reported in mzML
* mzMLMSnCount: Number of MSn scans reported in mzML
* DeconvMSnWithPeaksCount: Number of MSn scans containing masses after deconvolution
* DeconvMSnWithoutPeaksCount: Number of MSn scans lacking any masses after deconvolution
* DeconvMSnWithPeaksFraction: Ratio of DeconvMSnWithPeaksCount over mzMLMSnCount

The following metrics ask about potential duplication among MSn scans in the msAlign file:
* Redundancy: Of all possible MSn-MSn comparisons, what percentage found high similarity?
* HighestDegree: How many other MSn scans were found to be similar to the most "popular" MSn scan?
* LargestComponentSize: If we pull out the largest set of similar spectra for this LC-MS/MS experiment how many MSn scans does it contain?
* ComponentCount: How many different sets of similar spectra are there among the MSn scans from this deconvolved LC-MS/MS?

Next come dissociation statistics:
* mzMLHCDCount: count of mzML MSns from HCD
* mzMLCIDCount: count of mzML MSns from CID
* mzMLETDCount: count of mzML MSns from ETD
* mzMLECDCount: count of mzML MSns from ECD
* mzMLEThcDCount: count of mzML MSns from EThcD
* mzMLETciDCount: count of mzML MSns from ETciD

These metrics describe distributions by suppying minimum, 1st quartile, median, 3rd quartile, and maximum:
* mzMLPreZ: distribution of precursor charges from mzML file
* DeconvPreZ: distribution of precursor charges from deconvolution
* DeconvPreMass: distribution of precursor mass from deconvolution
* mzMLPeakCount: distribution of MSn peak counts from mzML file
* DeconvPeakCount: distribution of MSn peak counts from deconvolution
* AALinkCount: distribution of possible amino acid gap counts among MSn scans
* TagLength: distribution of longest possible sequence tag lengths among MSn scans

Two metrics attempt to summarize de novo "successes" across the LC-MS/MS experiment
* AALinkCountAbove2: asks how many MSn scans bracketed at least three amino acid masses.
* LongestTagAbove2: asks how many MSn scans bracketed at least three consecutive amino acid masses.

# TDAuditor-byMSn.tsv for each MSn
The file reports a relatively small set of metrics for each MSn scan in all input files:
* SourceFile: file Name
* NativeID: the format used by this instrument vendor to identify each MS and MSn scan
* ScanNumber: the identifier used to link scans reported in deconvolution
* ScanStartTime: the retention time at which this MS/MS was collected
* mzMLDissociation: a string summarizing the type of fragmentation applied
* mzMLPreZ: precursor charge as reported by instrument software
* DeconvPreZ: precursor charge as reported by deconvolution software
* DeconvPreMass: precursor Mass as reported by deconvolution software
* mzMLPeakCount: number of m/z values with intensity in mzML peaklist
* DeconvPeakCount: number of masses with intensities reported by deconvolution software
* Degree: how many other MSn scans were found to be similar to this one?
* ComponentNumber: a label one can use to ask which other MSn scans were connected by similarity to this one
* AALinkCount: how many pairs of deconvolved masses in this MSn were separated by amino acid masses?
* LongestTag: what is the longest AA sequence that can be inferred in this MSn scan by an unbroken chain of putative fragment masses?
