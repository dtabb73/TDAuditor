using System;
using PSI_Interface;
using PSI_Interface.MSData;
using PSI_Interface.CV;

namespace TDAuditor
{
	class Peak
	{
		public double Mass = 0;
		public float Intensity = 0;
		public Peak Next = null;

		public Peak()
		{
		}

		public Peak(double pMass, float pIntensity)
		{
			Mass = pMass;
			Intensity = pIntensity;
		}
	}

	class PeakPair
	{
		public double MassSum = 0;
		public double IntensityProduct = 0;
		public int PairSetPairCount = 0;
		public double PairSetIntensityProductSum = 0;
		public double PairSetMassAverage = 0;
		public double PairSetCharge = 0;
		public double PairSetScore = 0;
		public double PairSetPoisson = 0;
		public PeakPair Next = null;
		public void PrintAll()
		{
			Console.WriteLine("MassSum\tIntensityProduct");
			PeakPair PRunner = this.Next;
			while (PRunner != null)
            {
				Console.WriteLine(Convert.ToString(PRunner.MassSum) + "\t" + Convert.ToString(PRunner.IntensityProduct));
				PRunner = PRunner.Next;
            }
			Console.WriteLine();
        }
		public PeakPair QuickSortByMassSum(PeakPair Trailer)
		{
			PeakPair Before = new PeakPair();
			PeakPair After = new PeakPair();
			PeakPair Placeholder;
			PeakPair Placeholder2;
			while (this.Next != null)
            {
				Placeholder = this.Next;
				this.Next = Placeholder.Next;
				if (Placeholder.MassSum < this.MassSum)
				{
					//relocate Placeholder into the list Before
					Placeholder2 = Before.Next;
					Before.Next = Placeholder;
					Placeholder.Next = Placeholder2;
				}
				else
                {
					//relocate Placeholder into the list After
					Placeholder2 = After.Next;
					After.Next = Placeholder;
					Placeholder.Next = Placeholder2;
                }
            }
			if (After.Next == null)
			{
				this.Next = Trailer;
			}
			else
			{
				this.Next = After.Next.QuickSortByMassSum(Trailer);
			}
			if (Before.Next == null)
			{
				return this;
			}
			else
			{
				return Before.Next.QuickSortByMassSum(this);
			}
		}

		public void QuickSortByMassSum()
        {
			this.Next = this.Next.QuickSortByMassSum(null);
        }

		public double ThisZProbability()
		{
			return (Math.Abs(this.PairSetCharge - Math.Round(this.PairSetCharge)) / 0.5);
		}

		public double ThisPairProbability(double MassSumRange, int AllPairCounts, double ThisRangePPM)
        {
			double MassSumRangeWidth = this.MassSum * ThisRangePPM / 1000000.0;
			double mu = (AllPairCounts * MassSumRangeWidth) / MassSumRange;
			double x = Convert.ToDouble(this.PairSetPairCount);
			double factx = 1;
			for (double i=x;i>0;i--)
            {
				factx = factx * i;
            }
			double Probability = (Math.Exp(-mu) * Math.Pow(mu, x) / factx);
			return (Probability);
        }
		public void AssessPairSets(double MassSumRange, int AllPairCounts, double ppmTolerance, double PrecursorMZMinusProton)
        {
			PeakPair PPRunner = this.Next;
			PeakPair PPRunner2 = null;
			double MassSumByIntensity;
			double MassSumHighestBound;
			while (PPRunner != null)
            {
				MassSumHighestBound = (PPRunner.MassSum * ppmTolerance / 1000000.0) + PPRunner.MassSum;
				PPRunner.PairSetPairCount = 1;
				PPRunner.PairSetIntensityProductSum = PPRunner.IntensityProduct;
				MassSumByIntensity = PPRunner.IntensityProduct * PPRunner.MassSum;
				PPRunner2 = PPRunner.Next;
				while ((PPRunner2 != null) && (PPRunner2.MassSum < MassSumHighestBound))
                {
					PPRunner.PairSetPairCount++;
					PPRunner.PairSetIntensityProductSum += PPRunner2.IntensityProduct;
					MassSumByIntensity += PPRunner2.IntensityProduct * PPRunner2.MassSum;
					PPRunner2 = PPRunner2.Next;
                }
				PPRunner.PairSetMassAverage = MassSumByIntensity / PPRunner.PairSetIntensityProductSum;
				//FIXME: I believe this next line assumes that Xtract All was performed in neutral mass rather than MH+ mode
				PPRunner.PairSetCharge = PPRunner.PairSetMassAverage / PrecursorMZMinusProton;
				PPRunner.PairSetPoisson = PPRunner.ThisPairProbability(MassSumRange, AllPairCounts, ppmTolerance);
				PPRunner.PairSetScore = PPRunner.ThisZProbability() * PPRunner.PairSetPoisson;
				PPRunner = PPRunner.Next;
            }
        }
		public PeakPair QuickSortByPairData(PeakPair Trailer)
		{
			PeakPair Before = new PeakPair();
			PeakPair After = new PeakPair();
			PeakPair Placeholder;
			PeakPair Placeholder2;
			while (this.Next != null)
			{
				Placeholder = this.Next;
				this.Next = Placeholder.Next;
				// if ( (Placeholder.PairSetPairCount > this.PairSetPairCount) ||
				// 	( (Placeholder.PairSetPairCount == this.PairSetPairCount) && (Placeholder.PairSetIntensityProductSum > this.PairSetIntensityProductSum) ) )
				if (Placeholder.PairSetScore < this.PairSetScore)
				{
					//relocate Placeholder into the list Before
					Placeholder2 = Before.Next;
					Before.Next = Placeholder;
					Placeholder.Next = Placeholder2;
				}
				else
				{
					//relocate Placeholder into the list After
					Placeholder2 = After.Next;
					After.Next = Placeholder;
					Placeholder.Next = Placeholder2;
				}
			}
			if (After.Next == null)
			{
				this.Next = Trailer;
			}
			else
			{
				this.Next = After.Next.QuickSortByPairData(Trailer);
			}
			if (Before.Next == null)
			{
				return this;
			}
			else
			{
				return Before.Next.QuickSortByPairData(this);
			}
		}

		public void QuickSortByPairData()
		{
			this.Next = this.Next.QuickSortByPairData(null);
		}

	}

	class MSSpectrum
	{
		double Proton = 1.007276466621;
		double ppmError = 10.0;
		public String FILE_NAME = "";
		public int SCANS = 0;
		public float RETENTION_TIME = 0;
		public int LEVEL = 1;
		public Peak Peaks = new Peak();
		public MSSpectrum Next = null;
		// Other fields we want for tandem mass spectra
		public String ACTIVATION = "HCD";
		public int ID = 0;
		public int MS_ONE_ID = 0;
		public int MS_ONE_SCAN = 0;
		public MSSpectrum PrecedingMS1 = null;
		public double PRECURSOR_MZ = 0;
		public int PRECURSOR_CHARGE = 0;
		public double PRECURSOR_MASS = 0;
		public float PRECURSOR_INTENSITY = 0;
		// Which charge detection methods produced results?
		public bool ZMS1Lookup = false;
		public bool ZMS2Lookup = false;
		public bool ZMS2Complements = false;
		// These defaults must serve if the mzML doesn't tell us the isolation width!
		public double isolation_lower_offset = 1.0;
		public double isolation_upper_offset = 1.0;

		public MSSpectrum()
		{
		}

		public MSSpectrum(int pSCANS, String pFILE_NAME, float pRetentionTime)
		{
			SCANS = pSCANS;
			FILE_NAME = pFILE_NAME;
			RETENTION_TIME = pRetentionTime;
		}

		public int PkCount()
		{
			int Count = 0;
			Peak PRunner = this.Peaks;
			while (PRunner != null)
            {
				Count++;
				PRunner = PRunner.Next;
            }
			return Count;
		}

		public double TriangleIntensity(double TargetMass, double IsoLower, double IsoUpper, Peak ToCheck)
        {
			// It applies a scaling to the intensity of deconvolved masses, though, with a weight of 1.0 when the peak is exactly at the TargetMass.
			// It applies a weight of 0.0 when the peak is the isolation width below the TargetMass or when the peak is the isolation width above the TargetMass.
			double MassWeight;
			if (ToCheck.Mass > TargetMass)
            {
				MassWeight = 1.0 - ((ToCheck.Mass - TargetMass) / IsoUpper);
            }
			else
            {
				MassWeight = 1.0 - ((TargetMass - ToCheck.Mass) / IsoLower);
            }
			return (MassWeight * ToCheck.Intensity);
		}
		public Peak FindWithinIsolation(double TargetMass, double IsoLower, double IsoUpper)
		{
			// This code seeks intense ions that fall within the TargetLoMass to TargetHiMass range in the deconvolved MS scan.
			double TargetLoMass = TargetMass - IsoLower;
			double TargetHiMass = TargetMass + IsoUpper;
			Peak PRunner = Peaks.Next;
			Peak BestPeakSoFar = null;
			double BestNormalizedIntensity = 0.0;
			double ThisNormalizedIntensity;
			while ( (PRunner != null) && (PRunner.Mass < TargetHiMass) )
			{
				if (PRunner.Mass > TargetLoMass)
                {
					ThisNormalizedIntensity = TriangleIntensity(TargetMass, IsoLower, IsoUpper, PRunner);
					if (ThisNormalizedIntensity > BestNormalizedIntensity)
					{
						BestPeakSoFar = PRunner;
						BestNormalizedIntensity = ThisNormalizedIntensity;
					}
				}
				PRunner = PRunner.Next;
			}
			return BestPeakSoFar;
		}
		public void SeekChargeAndMass(MSSpectrum MS1Scans, bool MHnotNeutral, int MaxCharge, string FileBase)
		{
			//This code assumes MS1 scans are present and that all scans are reported in order of retention time, not separated by MS level.
			MSSpectrum Runner = this.Next;
			MSSpectrum MS1Runner = MS1Scans;
			Peak FoundPeakMS1 = null;
			Peak FoundPeakMS2 = null;
			Peak BestPeakSoFarMS1 = null;
			Peak BestPeakSoFarMS2 = null;
			int BestZSoFarMS1 = 0;
			int BestZSoFarMS2 = 0;
			Peak LoRunner;
			Peak HiRunner;
			PeakPair Pairs;
			PeakPair PPRunner;
			double TargetMass;
			double BestTriangleIntensityMS1;
			double ThisTriangleIntensityMS1;
			double BestTriangleIntensityMS2;
			double ThisTriangleIntensityMS2;
			int NoMatchMS1 = 0;
			int NoMatchMS2 = 0;
			int DecidedOnMS1 = 0;
			int DecidedOnMS2 = 0;
			int DecidedOnMS2Complements = 0;
			int DecidedOnDefault = 0;
			int MS2Count = 0;
			int ChargeLooper;
			int AllPairCount = 0;
			double LoMassSum = 0;
			double HiMassSum = 0;
			double MassSumWindow = ppmError * Math.Sqrt(2.0);
			Console.WriteLine("Assessing likely precursor mass and charge...");
			using StreamWriter OutFile = new(FileBase + "-MassChargeDetermination.tsv");
			OutFile.WriteLine("Scan\tPreMZ\tRT\tPkCount\tZfromMS1\tZfromMS2\tZfromComplements");
			while (Runner != null)
            {
				// Attempt to find complementary pairs of fragments to estimate precursor
				Pairs = new PeakPair();
				PPRunner = Pairs;
				LoRunner = Runner.Peaks.Next;
				AllPairCount = 0;
				LoMassSum = 0;
				HiMassSum = 0;
				while (LoRunner != null)
                {
					HiRunner = LoRunner.Next;
					while (HiRunner != null)
                    {
						AllPairCount++;
						PPRunner.Next = new PeakPair();
						PPRunner = PPRunner.Next;
						PPRunner.MassSum = LoRunner.Mass + HiRunner.Mass;
						if (LoMassSum == 0)
						{
							LoMassSum = PPRunner.MassSum;
						}
						HiMassSum = PPRunner.MassSum;
						PPRunner.IntensityProduct = Math.Log(LoRunner.Intensity) + Math.Log(HiRunner.Intensity);
						HiRunner = HiRunner.Next;
                    }
					LoRunner = LoRunner.Next;
                }
				if (Pairs.Next != null)
				{
					Pairs.QuickSortByMassSum();
					Pairs.AssessPairSets(HiMassSum - LoMassSum, AllPairCount, MassSumWindow, Runner.PRECURSOR_MZ - Proton);
					Pairs.QuickSortByPairData();
					PPRunner = Pairs.Next;
					if (PPRunner != null)
					{
						Runner.ZMS2Complements = true;
						/*
						OutFile.WriteLine("Compl Mass\t" + PPRunner.PairSetMassAverage +
						"\tZ\t" + Math.Round(PPRunner.PairSetCharge) +
						"\tIntensity\t" + PPRunner.IntensityProduct +
						"\tMS2 Complementary Pairs\t" + PPRunner.PairSetPairCount +
						"\tPair Poisson\t" + PPRunner.PairSetPoisson +
						// ThisPairProbability(HiMassSum - LoMassSum, AllPairCount, MassSumWindow) +
						"\tZ Probability\t" + PPRunner.ThisZProbability());
						*/
					}
				}

				// Seek the precusor mass in the MS1 that precedes this MS2 scan;
				// also seek the precursor mass in this MS2.
				BestPeakSoFarMS1 = null;
				BestPeakSoFarMS2 = null;
				BestZSoFarMS1 = 0;
				BestZSoFarMS2 = 0;
				BestTriangleIntensityMS1 = 0;
				BestTriangleIntensityMS2 = 0;
				// Advance the runner in MS spectra to the one preceding this MS/MS scan.
				while ((MS1Runner != null) && (MS1Runner.Next != null) && (MS1Runner.Next.SCANS < Runner.SCANS))
				{
					MS1Runner = MS1Runner.Next;
				}
				if (MS1Runner == null)
				{
					Console.Error.WriteLine("While seeking a precursor for scan " + Runner.SCANS + ", I ran out of MS1 scans.");
					Environment.Exit(2);
				}
				MS2Count++;
				Runner.MS_ONE_SCAN = MS1Runner.SCANS;
				// Why consider only precursors of +2 and above?  Because we often find intense signals at the original m/z that distract from real (and higher) precursor charges.
				for (ChargeLooper = 2; ChargeLooper <= MaxCharge; ChargeLooper++)
				{
					// The PRECURSOR_MZ value is always protonated, with number of protons equal to charge
					if (MHnotNeutral)
					{
						TargetMass = (Runner.PRECURSOR_MZ - Proton) * ChargeLooper + Proton;
					}
					else
					{
						TargetMass = (Runner.PRECURSOR_MZ - Proton) * ChargeLooper;
					}
					FoundPeakMS1 = MS1Runner.FindWithinIsolation(TargetMass, Runner.isolation_lower_offset*ChargeLooper, Runner.isolation_upper_offset*ChargeLooper);
					if (FoundPeakMS1 != null)
					{
						ThisTriangleIntensityMS1 = MS1Runner.TriangleIntensity(TargetMass, Runner.isolation_lower_offset * ChargeLooper, Runner.isolation_upper_offset * ChargeLooper, FoundPeakMS1);
						// OutFile.WriteLine("HitMass\t" + BestPeak.Mass + "\tHitZ\t" + ChargeLooper + "\tHitIntensity\t" + BestPeak.Intensity + "\tHitTriangleIntensity\t" + ThisTriangleIntensity);
						if (ThisTriangleIntensityMS1 > BestTriangleIntensityMS1)
                        {
							BestPeakSoFarMS1 = FoundPeakMS1;
							BestZSoFarMS1 = ChargeLooper;
							BestTriangleIntensityMS1 = ThisTriangleIntensityMS1;
						}
					}
					FoundPeakMS2 = Runner.FindWithinIsolation(TargetMass, Runner.isolation_lower_offset * ChargeLooper, Runner.isolation_upper_offset * ChargeLooper);
					if (FoundPeakMS2 != null)
                    {
						ThisTriangleIntensityMS2 = Runner.TriangleIntensity(TargetMass, Runner.isolation_lower_offset * ChargeLooper, Runner.isolation_upper_offset * ChargeLooper, FoundPeakMS2);
						if (ThisTriangleIntensityMS2 > BestTriangleIntensityMS2)
                        {
							BestPeakSoFarMS2 = FoundPeakMS2;
							BestZSoFarMS2 = ChargeLooper;
							BestTriangleIntensityMS2 = ThisTriangleIntensityMS2;
                        }
                    }
				}
				if (BestPeakSoFarMS1 == null)
				{
					NoMatchMS1++;
				}
				else
				{
					Runner.ZMS1Lookup = true;
					/*
					OutFile.WriteLine("MS1 Mass\t" + BestPeakSoFarMS1.Mass + "\tZ\t" + BestZSoFarMS1 + "\tIntensity\t" +
						BestPeakSoFarMS1.Intensity + "\tTriangle\t" + (1.0 - (BestTriangleIntensityMS1 / BestPeakSoFarMS1.Intensity)));
					*/
				}
				if (BestPeakSoFarMS2 == null)
				{
					NoMatchMS2++;
				}
				else
				{
					Runner.ZMS2Lookup = true;
					/*
					OutFile.WriteLine("MS2 Mass\t" + BestPeakSoFarMS2.Mass + "\tZ\t" + BestZSoFarMS2 + "\tIntensity\t" +
						BestPeakSoFarMS2.Intensity + "\tTriangle\t" + (1.0 - (BestTriangleIntensityMS2 / BestPeakSoFarMS2.Intensity)));
					*/
				}
				// Now make a decision about which approach will be used to estimate this spectrum's precursor mass
				// TopPIC expects us to write neutral masses; FIXME!
				string TextMS1 = "\tNA";
				string TextMS2 = "\tNA";
				string TextCompl = "\tNA";
				if (BestPeakSoFarMS1 != null) TextMS1 = "\t" + Convert.ToString(BestZSoFarMS1);
				if (BestPeakSoFarMS2 != null) TextMS2 = "\t" + Convert.ToString(BestZSoFarMS2);
				if (Pairs.Next != null)       TextCompl = "\t" + Convert.ToString(Math.Round(Pairs.Next.PairSetCharge));
				if (BestPeakSoFarMS1 != null)
                {
					Runner.PRECURSOR_MASS = BestPeakSoFarMS1.Mass;
					Runner.PRECURSOR_CHARGE = BestZSoFarMS1;
					DecidedOnMS1++;
				}
				else if (BestPeakSoFarMS2 != null)
                {
					Runner.PRECURSOR_MASS = BestPeakSoFarMS2.Mass;
					Runner.PRECURSOR_CHARGE = BestZSoFarMS2;
					DecidedOnMS2++;
                }
				else if (Pairs.Next != null)
                {
					Runner.PRECURSOR_MASS = Pairs.Next.PairSetMassAverage;
					Runner.PRECURSOR_CHARGE = (int)Math.Round(Pairs.Next.PairSetCharge);
					DecidedOnMS2Complements++;
				}
				else
                {
					Runner.PRECURSOR_MZ = Runner.PRECURSOR_MZ;
					Runner.PRECURSOR_CHARGE = 1;
					DecidedOnDefault++;
                }
				OutFile.WriteLine(Runner.SCANS + "\t" + Runner.PRECURSOR_MZ + "\t" + (Runner.RETENTION_TIME) + "\t" + Runner.PkCount() + TextMS1 + TextMS2 + TextCompl);
				Runner = Runner.Next;
			}
			Console.WriteLine(NoMatchMS1 + " of " + MS2Count + " MS/MS scans lacked a precursor of charge <= " + MaxCharge + " within isolation window of preceding MS1 scans.");
			Console.WriteLine(NoMatchMS2 + " of " + MS2Count + " MS/MS scans lacked a precursor of charge <= " + MaxCharge + " within isolation window of MS2 scans.\n");
			Console.WriteLine(DecidedOnMS1 + " precursors were recorded as detected in MS1 scan.");
			Console.WriteLine(DecidedOnMS2 + " precursors were recorded as detected in MS2 scan.");
			Console.WriteLine(DecidedOnMS2Complements + " precursors were recorded by summing complementary ions.");
			Console.WriteLine(DecidedOnDefault + " precursors were recorded as +1s.");
		}
		public void PrintTDAuditor(MSSpectrum MS1s, int MaxCharge, string FileBase)
        {
			int CountMS1 = 0;
			int CountMS2 = 0;
			int CountCID = 0;
			int CountHCD = 0;
			int CountECD = 0;
			int CountETD = 0;
			int CountUVPD = 0;
			int CountEThcD = 0;
			int CountETciD = 0;
			// What combination of precursor charge methods would possibly work for each spectrum?
			int ZMethodNone = 0;
			int ZMethodMS1 = 0;
			int ZMethodMS2 = 0;
			int ZMethodComp = 0;
			int ZMethodMS1MS2 = 0;
			int ZMethodMS1Comp = 0;
			int ZMethodMS2Comp = 0;
			int ZMethodMS1MS2Comp = 0;
			// Store precursor charge distribution
			int[] PrecursorCharges = new int[MaxCharge + 1];
			int CountHigherCharge = 0;
			MSSpectrum MSRunner = MS1s.Next;
			while (MSRunner != null)
			{
				CountMS1++;
				MSRunner = MSRunner.Next;
			}
			// Now assay the MS/MS scans
			MSRunner = this.Next;
			while (MSRunner != null)
			{
				CountMS2++;
				switch(MSRunner.ACTIVATION)
                {
					case "CID":
						CountCID++;
						break;
					case "HCD":
						CountHCD++;
						break;
					case "ECD":
						CountECD++;
						break;
					case "ETD":
						CountETD++;
						break;
					case "UVPD":
						CountUVPD++;
						break;
					case "EThcD":
						CountEThcD++;
						break;
					case "ETciD":
						CountETciD++;
						break;
                }
				if (MSRunner.PRECURSOR_CHARGE > MaxCharge)
				{
					CountHigherCharge++;
				}
				else
				{
					PrecursorCharges[MSRunner.PRECURSOR_CHARGE]++;
				}
				if (MSRunner.ZMS1Lookup)
                {
					if (MSRunner.ZMS2Lookup)
                    {
						if (MSRunner.ZMS2Complements)
                        {
							ZMethodMS1MS2Comp++;
                        }
                        else
                        {
							ZMethodMS1MS2++;
                        }
                    }
                    else
                    {
						if (MSRunner.ZMS2Complements)
						{
							ZMethodMS1Comp++;
						}
						else
						{
							ZMethodMS1++;
						}
					}
                }
                else
                {
					if (MSRunner.ZMS2Lookup)
					{
						if (MSRunner.ZMS2Complements)
						{
							ZMethodMS2Comp++;
						}
						else
						{
							ZMethodMS2++;
						}
					}
					else
					{
						if (MSRunner.ZMS2Complements)
						{
							ZMethodComp++;
						}
						else
						{
							ZMethodNone++;
						}
					}
				}
                MSRunner = MSRunner.Next;
			}
			using StreamWriter OutFile = new(FileBase + "-TDAuditor.tsv");
			OutFile.WriteLine("MS1Count\t" + CountMS1);
			OutFile.WriteLine("MS2Count\t" + CountMS2);
			OutFile.WriteLine("MS2Count-CID\t" + CountCID);
			OutFile.WriteLine("MS2Count-HCD\t" + CountHCD);
			OutFile.WriteLine("MS2Count-ECD\t" + CountECD);
			OutFile.WriteLine("MS2Count-ETD\t" + CountETD);
			OutFile.WriteLine("MS2Count-UVPD\t" + CountUVPD);
			OutFile.WriteLine("MS2Count-EThcD\t" + CountEThcD);
			OutFile.WriteLine("MS2Count-ETciD\t" + CountETciD);
			OutFile.WriteLine();

			OutFile.WriteLine("ZMethod-None\t" + ZMethodNone);
			OutFile.WriteLine("ZMethod-MS1LookupOnly\t" + ZMethodMS1);
			OutFile.WriteLine("ZMethod-MS2LookupOnly\t" + ZMethodMS2);
			OutFile.WriteLine("ZMethod-ComplementsOnly\t" + ZMethodComp);
			OutFile.WriteLine("ZMethod-MS1AndMS2\t" + ZMethodMS1MS2);
			OutFile.WriteLine("ZMethod-MS1AndComplements\t" + ZMethodMS1Comp);
			OutFile.WriteLine("ZMethod-MS2AndComplements\t" + ZMethodMS2Comp);
			OutFile.WriteLine("ZMethod-All\t" + ZMethodMS1MS2Comp);
			OutFile.WriteLine();

			for (int z = 0; z<=MaxCharge; z++)
            {
				OutFile.WriteLine("MS2-PrecZ-" + String.Format("{0:000}", z) + "\t" + PrecursorCharges[z]);
            }
			OutFile.WriteLine("MS2-PrecZ-more\t" + CountHigherCharge);
		}
		public void PrintMSAlign(string FileBase)
		{
			MSSpectrum Runner = this.Next;
			using StreamWriter OutFile = new(FileBase + ".msalign");
			Peak PkRunner;
			int PkCounter;
			while (Runner != null)
			{
				PkCounter = 0;
				OutFile.WriteLine("BEGIN IONS");
				OutFile.WriteLine("ID=" + Runner.ID);
				OutFile.WriteLine("FRACTION_ID=0");
				OutFile.WriteLine("FILE_NAME=" + Runner.FILE_NAME);
				OutFile.WriteLine("SCANS=" + Runner.SCANS);
				OutFile.WriteLine("RETENTION_TIME=" + Runner.RETENTION_TIME);
				OutFile.WriteLine("LEVEL=" + Runner.LEVEL);
				OutFile.WriteLine("ACTIVATION=" + Runner.ACTIVATION);
				OutFile.WriteLine("MS_ONE_ID=" + Runner.MS_ONE_ID);
				OutFile.WriteLine("MS_ONE_SCAN=" + Runner.MS_ONE_SCAN);
				OutFile.WriteLine("PRECURSOR_MZ=" + Runner.PRECURSOR_MZ);
				OutFile.WriteLine("PRECURSOR_CHARGE=" + Runner.PRECURSOR_CHARGE);
				OutFile.WriteLine("PRECURSOR_MASS=" + Runner.PRECURSOR_MASS);
				OutFile.WriteLine("PRECURSOR_INTENSITY=" + Runner.PRECURSOR_INTENSITY);
				PkRunner = Runner.Peaks.Next;
				while (PkRunner != null)
				{
					OutFile.WriteLine(PkRunner.Mass + "\t" + PkRunner.Intensity + "\t1");
					PkCounter++;
					PkRunner = PkRunner.Next;
				}
				OutFile.WriteLine("END IONS");
				// OutFile.Error.WriteLine(Runner.SCANS + "\t" + PkCounter);
				OutFile.WriteLine();
				Runner = Runner.Next;
			}
		}
	}


	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine("Supply the name of the FreeStyle Xtract-deconvolved mzML file for processing.");
				Environment.Exit(1);
			}
			string FileString = args[0];
			string FileBase = Path.GetFileNameWithoutExtension(FileString);
			using (var reader = new SimpleMzMLReader(FileString, false, true))
			{
				MSSpectrum MS1Spectra = new MSSpectrum();
				MSSpectrum MS1Runner = MS1Spectra;
				MSSpectrum MS2Spectra = new MSSpectrum();
				MSSpectrum MS2Runner = MS2Spectra;
				Peak PRunner;
				int MS1Count = 0;
				int MS2Count = 0;
				int MaxCharge = 50;
				foreach (var spec in reader.ReadAllSpectra(true))
				{
					// Console.WriteLine(spec.ScanNumber + "\t" + spec.Peaks.Length + "\t" + spec.MsLevel);
					if (spec.MsLevel == 1)
					{
						MS1Count++;
						MS1Runner.Next = new MSSpectrum(spec.ScanNumber, FileString, 60.0f * Convert.ToSingle(spec.ScanStartTime));
						MS1Runner = MS1Runner.Next;
						MS1Runner.LEVEL = 1;
						PRunner = MS1Runner.Peaks;
						foreach (var thispeak in spec.Peaks)
						{
							PRunner.Next = new Peak(thispeak.Mz, Convert.ToSingle(thispeak.Intensity));
							PRunner = PRunner.Next;
						}
					}
					else
					{
						MS2Count++;
						MS2Runner.Next = new MSSpectrum(spec.ScanNumber, FileString, 60.0f * Convert.ToSingle(spec.ScanStartTime));
						MS2Runner = MS2Runner.Next;
						MS2Runner.LEVEL = 2;
						MS2Runner.ID = MS2Count-1;
						MS2Runner.MS_ONE_ID = MS1Count-1;
						PRunner = MS2Runner.Peaks;
						foreach (var thispeak in spec.Peaks)
						{
							PRunner.Next = new Peak(thispeak.Mz, Convert.ToSingle(thispeak.Intensity));
							PRunner = PRunner.Next;
						}
						MS2Runner.PrecedingMS1 = MS1Runner;
						//Set Activation type and MS1 scan info
						if (MS2Runner.PrecedingMS1 != null)
						{
							MS2Runner.MS_ONE_SCAN = MS2Runner.PrecedingMS1.SCANS;
						}
						if (spec.Precursors.Count > 0 && spec.Precursors[0].IsolationWindow != null)
                        {
							foreach (var cvParam in spec.Precursors[0].IsolationWindow.CVParams)
                            {
								if (cvParam.TermInfo.Cvid == CV.CVID.MS_isolation_window_lower_offset)
								{
									MS2Runner.isolation_lower_offset = Convert.ToDouble(cvParam.Value);
								}
								else if (cvParam.TermInfo.Cvid == CV.CVID.MS_isolation_window_upper_offset)
								{
									MS2Runner.isolation_upper_offset = Convert.ToDouble(cvParam.Value);
								}
							}

						}
						if (spec.Precursors.Count > 0 && spec.Precursors[0].SelectedIons.Count > 0)
						{
							foreach (var cvParam in spec.Precursors[0].SelectedIons[0].CVParams)
							{
								switch (cvParam.TermInfo.Cvid)
								{
									case CV.CVID.MS_selected_ion_m_z:
									case CV.CVID.MS_selected_precursor_m_z:
										MS2Runner.PRECURSOR_MZ = Convert.ToDouble(cvParam.Value);
										break;

									case CV.CVID.MS_peak_intensity:
										MS2Runner.PRECURSOR_INTENSITY = Convert.ToSingle(cvParam.Value);
										break;					
								}
							}
							//The following dissociation logic is adapted from https://github.com/PNNL-Comp-Mass-Spec/MASIC/blob/master/DataInput/DataImportMSXml.cs
							var activationMethods = new SortedSet<string>();
							var supplementalMethods = new SortedSet<string>();
							foreach (var cvParam in spec.Precursors[0].CVParams)
							{
								switch (cvParam.TermInfo.Cvid)
								{
									case CV.CVID.MS_collision_induced_dissociation:
									case CV.CVID.MS_low_energy_collision_induced_dissociation:
									case CV.CVID.MS_in_source_collision_induced_dissociation:
									case CV.CVID.MS_trap_type_collision_induced_dissociation:
										activationMethods.Add("CID");
										break;

									case CV.CVID.MS_electron_capture_dissociation:
										activationMethods.Add("ECD");
										break;

									case CV.CVID.MS_beam_type_collision_induced_dissociation:
										activationMethods.Add("HCD");
										break;

									case CV.CVID.MS_photodissociation:
										// ReSharper disable once StringLiteralTypo
										activationMethods.Add("UVPD");
										break;

									case CV.CVID.MS_electron_transfer_dissociation:
										activationMethods.Add("ETD");
										break;

									case CV.CVID.MS_electron_transfer_higher_energy_collision_dissociation:
										activationMethods.Add("EThcD");
										break;

									case CV.CVID.MS_supplemental_beam_type_collision_induced_dissociation:
										supplementalMethods.Add("HCD");
										break;

									case CV.CVID.MS_supplemental_collision_induced_dissociation:
										supplementalMethods.Add("CID");
										break;
								}
								if (activationMethods.Contains("ETD"))
								{
									if (supplementalMethods.Contains("CID"))
									{
										activationMethods.Remove("ETD");
										activationMethods.Add("ETciD");
									}
									else if (supplementalMethods.Contains("HCD"))
									{
										activationMethods.Remove("ETD");
										activationMethods.Add("EThcD");
									}
								}
								MS2Runner.ACTIVATION = String.Join(",", activationMethods);
							}
						}
					}
				}
				Console.WriteLine("MS1s: " + MS1Count + "\tMS2s: " + MS2Count);
				MS2Spectra.SeekChargeAndMass(MS1Spectra, false, MaxCharge, FileBase);
				MS2Spectra.PrintMSAlign(FileBase);
				MS2Spectra.PrintTDAuditor(MS1Spectra, MaxCharge, FileBase);
			}
		}
	}
}