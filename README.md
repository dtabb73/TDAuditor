# TDAuditor: Assessing LC-MS/MS quality for top-down proteomics
The analytical chemistry and bioinformatics to identify intact proteoforms has developed quite substantially over the last decade.  The complexity of LC-MS/MS experiments and identification algorithms for top-down proteomics can pose a particular challenge when an experiment does not identify the proteoforms that were expected.  Was no tandem mass spectrum acquired for that precursor ion?  Did deconvolution successfully infer its charge state?  Were the fragment masses resulting from its dissociation viable for identification?  TDAuditor is intended to assist top-down proteome researchers in characterizing the LC-MS/MS data that they have acquired, appraising the impact of deconvolution on a scan-by-scan basis.

Practically speaking, TDAuditor collates data from mzML files (often created by ProteoWizard msConvert) and their corresponding msAlign files (typically created by TopFD in the TopPIC Suite or by FLASHDeconv from OpenMS 3.0).  The software is implemented in C#, a .NET language, and can be run in Microsoft Windows.  It creates reports in tab-separated values text files and exports visualizations in GraphViz .DOT text files.  The reports include TDAuditor-byRun.tsv, where each row represents an input mzML/msAlign pair and each column supplies a particular metric for that LC-MS/MS experiment, and TDAuditor-byMSn.tsv, where each row represents an MS/MS scan from the input data and metrics characterize these individual scans.

# TDAuditor-byRun.tsv Report for each LC-MS/MS
The metrics reported for each LC-MS/MS begin with some essential details:
* File Name
* Instrument Model
* Instrument Serial Number
* Start Time Stamp
* Retention Time Duration

It continues with scan counts:
* mzML MS1 count
* mzML MSn count
* msAlign occupied MSn count (how many of the msAlign MS/MS scans contain masses after deconvolution?)
* msAlign empty MSn count
* Fraction of mzML MSns represented by occupied MSns in msAlign file

The following metrics ask about potential duplication among MSn scans in the msAlign file:
* Redundancy (of all possible MSn-MSn comparisons, how many were similar by sharing a large number of overlapping fragment masses?)
* Highest Degree (how many other MSn scans were found to be similar to the most "popular" MSn scan?)
* Largest Component Size (If we pull out the largest set of similar spectra for this LC-MS/MS experiment how many MSn scans does it contain?)
* Component Count (How many different sets of similar spectra are there among the MSn scans of this msAlign file?)

Next come dissociation statistics:
* count of mzML MSns from HCD
* count of mzML MSns from CID
* count of mzML MSns from ETD
* count of mzML MSns from ECD
* count of mzML MSns from EThcD
* count of mzML MSns from ETciD

The remaining metrics describe distributions by suppying minimum, 1st quartile, median, 3rd quartile, and maximum:
* distribution of precursor charges from mzML file
* distribution of precursor charges from msAlign file
* distribution of MSn peak counts from mzML file
* distribution of MSn peak counts from msAlign file
* distribution of possible amino acid gap counts among MSn scans in msAlign file
* distribution of longest possible sequence tag lengths among MSn scans in msAlign file

# TDAuditor-byMSn.tsv for each MSn
The file reports a relatively small set of metrics for each MSn scan in all input files:
* File Name
* NativeID (the format used by this instrument vendor to identify each MS and MSn scan)
* Scan Number (the identifier used to link scans reported in the msAlign format)
* Scan Start Time
* mzML Dissociation Type
* mzML Precursor Charge
* msAlign Precursor Charge
* msAlign Precursor Mass
* mzML Peak Count
* msAlign Peak Count
* Degree (how many other MSn scans were found to be similar to this one?)
* Component Number (a label one can use to ask which other MSn scans were connected by similarity to this one)
* Amino Acid Link Count (how many pairs of masses in this msAlign MSn scan were separated by amino acid masses?)
* Longest Tag (what is the longest sequence that can be inferred in this MSn scan by an unbroken chain of putative fragment masses?)
