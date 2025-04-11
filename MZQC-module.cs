// This is a **experimental** mzQC library implementation for C#
// It is tested only to the extent of the TDAuditor usecase. Beyond, there be dragons.
// To parse mzQC JSON data, add the MZQC-module ('Newtonsoft.Json' dependency to your 
//  assembly) to your project .cs group, then for mzQC input do for example:
    // string contents = File.ReadAllText(@"individual-runs.mzQC");
    // var iomzqc = Mzqc.FromJson(contents);
    // File.WriteAllText(@"regurgitated.mzqc", iomzqc.ToJson());
// for ground-up mzQC design, do for example:            
    // var mzqc = new MzqcContent { Description = "Sample" };  // YOU need to add more mzQC content!
    // var file = new Mzqc {MzqcContent = mzqc};
    // File.WriteAllText(@"out.mzqc", file.ToJson());

namespace MzqcCsLib
{
    using System;
    using System.Collections.Generic;

    using System.Globalization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// JSON schema specifying the mzQC format v1.0.0 developed by the HUPO-PSI Quality Control
    /// working group (http://psidev.info/groups/quality-control).
    public partial class Mzqc
    {
        /// <summary>
        /// Root element of an mzQC file.
        /// </summary>
        [JsonProperty("mzQC")]
        public MzqcContent MzqcContent { get; set; }
    }

    public partial class MzqcContent
    {
        /// Contact Address (mail/tel.) for getting in touch with given contact for a particular mzQC file
        [JsonProperty("contactAddress", NullValueHandling = NullValueHandling.Ignore)]
        public string ContactAddress { get; set; }

        /// Name of file creator or person chosen as dedicated contact a particular mzQC file.
        [JsonProperty("contactName", NullValueHandling = NullValueHandling.Ignore)]
        public string ContactName { get; set; }

        /// Collection of controlled vocabulary elements used to refer to the source of the used CV
        /// terms in the qualityMetric objects (and others).
        [JsonProperty("controlledVocabularies")]
        public List<ControlledVocabulary> ControlledVocabularies { get; set; }

        /// Creation date of the mzQC file.
        [JsonProperty("creationDate")]
        public DateTimeOffset CreationDate { get; set; }

        /// Description and comments about the mzQC file contents.
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// List of runQuality elements.
        [JsonProperty("runQualities", NullValueHandling = NullValueHandling.Ignore)]
        public List<BaseQuality> RunQualities { get; set; }

        /// List of setQuality elements.
        [JsonProperty("setQualities", NullValueHandling = NullValueHandling.Ignore)]
        public List<BaseQuality> SetQualities { get; set; }

        /// Version of the mzQC format.
        [JsonProperty("version")]
        public string Version { get; set; }

    }

    /// Element describing a controlled vocabulary used to refer to the source of the used CV
    /// terms in qualityMetric objects (and others).
    public partial class ControlledVocabulary
    {
        /// Full name of the controlled vocabulary.
        [JsonProperty("name")]
        public string Name { get; set; }

        /// Publicly accessible URI of the controlled vocabulary.
        [JsonProperty("uri")]
        public Uri Uri { get; set; }

        /// Version of the controlled vocabulary.
        [JsonProperty("version", NullValueHandling = NullValueHandling.Ignore)]
        public string Version { get; set; }
    }

    /// Element containing metadata and qualityMetrics for a single run.
    ///
    /// Base element from which both runQuality and setQuality elements are derived.
    ///
    /// Element containing metadata and qualityMetrics for a collection of related runs (set).
    public partial class BaseQuality
    {
        [JsonProperty("metadata")]
        public Metadata Metadata { get; set; }

        /// The collection of qualityMetrics for a particular runQuality or setQuality.
        [JsonProperty("qualityMetrics")]
        public List<QualityMetric> QualityMetrics { get; set; }
    }

    /// Metadata describing the QC analysis.
    public partial class Metadata
    {
        /// Software tool(s) used to generate the QC metrics.
        [JsonProperty("analysisSoftware")]
        public List<AnalysisSoftwareElement> AnalysisSoftware { get; set; }

        /// OPTIONAL list of cvParameter elements containing additional metadata about its parent
        /// runQuality/setQuality.
        [JsonProperty("cvParameters", NullValueHandling = NullValueHandling.Ignore)]
        public List<CvParameter> CvParameters { get; set; }

        /// List of input files from which the QC metrics have been generated.
        [JsonProperty("inputFiles")]
        public List<InputFile> InputFiles { get; set; }

        /// OPTIONAL label name. For setQuality, this a group name, lending itself for example as a
        /// axis labels for a plot. OPTIONAL.
        [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)]
        public string Label { get; set; }
    }

    /// Base element for a term that is defined in a controlled vocabulary, with OPTIONAL value.
    public partial class AnalysisSoftwareElement
    {
        /// Accession number identifying the term within its controlled vocabulary.
        [JsonProperty("accession")]
        public string Accession { get; set; }

        /// Definition of the controlled vocabulary term.
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// Name of the controlled vocabulary term describing the parameter.
        [JsonProperty("name")]
        public string Name { get; set; }

        /// Value of the parameter.
        [JsonProperty("value")]
        public object Value { get; set; }

        /// Publicly accessible URI of the software tool or documentation.
        [JsonProperty("uri")]
        public Uri Uri { get; set; }

        /// Version number of the software tool.
        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// Base element for a term that is defined in a controlled vocabulary, with OPTIONAL value.
    public partial class CvParameter
    {
        /// Accession number identifying the term within its controlled vocabulary.
        [JsonProperty("accession")]
        public string Accession { get; set; }

        /// Definition of the controlled vocabulary term.
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// Name of the controlled vocabulary term describing the parameter.
        [JsonProperty("name")]
        public string Name { get; set; }

        /// Value of the parameter.
        [JsonProperty("value")]
        public object Value { get; set; }
    }

    /// Input file used to generate the QC metrics.
    public partial class InputFile
    {
        /// Type of input file.
        [JsonProperty("fileFormat")]
        public CvParameter FileFormat { get; set; }

        /// Detailed properties of the input file.
        [JsonProperty("fileProperties", NullValueHandling = NullValueHandling.Ignore)]
        public List<CvParameter> FileProperties { get; set; }

        /// Unique file location. The file URI is RECOMMENDED to be publicly accessible.
        [JsonProperty("location")]
        public Uri Location { get; set; }

        /// Base file name. This MUST be unique across all inputFiles specified in the mzQC file.
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// Element containing the value and description of a QC metric defined in a controlled
    /// vocabulary.
    ///
    /// Base element for a term that is defined in a controlled vocabulary, with OPTIONAL value.
    public partial class QualityMetric
    {
        /// Accession number identifying the term within its controlled vocabulary.
        [JsonProperty("accession")]
        public string Accession { get; set; }

        /// Definition of the controlled vocabulary term.
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// Name of the controlled vocabulary term describing the parameter.
        [JsonProperty("name")]
        public string Name { get; set; }

        /// Value of the parameter.
        [JsonProperty("value")]
        public object Value { get; set; }

        /// One or more controlled vocabulary elements describing the unit of the metric.
        [JsonProperty("unit", NullValueHandling = NullValueHandling.Ignore)]
        public Unit? Unit { get; set; }
    }

    /// One or more controlled vocabulary elements describing the unit of the metric.
    public partial struct Unit
    {
        public CvParameter CvParameter;
        public List<CvParameter> CvParameterArray;

        public static implicit operator Unit(CvParameter CvParameter) => new Unit { CvParameter = CvParameter };
        public static implicit operator Unit(List<CvParameter> CvParameterArray) => new Unit { CvParameterArray = CvParameterArray };
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                UnitConverter.Singleton,
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }

    public partial class Mzqc
    {
        public static Mzqc FromJson(string json) => JsonConvert.DeserializeObject<Mzqc>(json);
    }

    public static class Serialize
    {
        public static string ToJson(this Mzqc self) => JsonConvert.SerializeObject(self);
    }

    internal class UnitConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(Unit) || t == typeof(Unit?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    var objectValue = serializer.Deserialize<CvParameter>(reader);
                    return new Unit { CvParameter = objectValue };
                case JsonToken.StartArray:
                    var arrayValue = serializer.Deserialize<List<CvParameter>>(reader);
                    return new Unit { CvParameterArray = arrayValue };
            }
            throw new Exception("Cannot unmarshal type Unit");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            var value = (Unit)untypedValue;
            if (value.CvParameterArray != null)
            {
                serializer.Serialize(writer, value.CvParameterArray);
                return;
            }
            if (value.CvParameter != null)
            {
                serializer.Serialize(writer, value.CvParameter);
                return;
            }
            throw new Exception("Cannot marshal type Unit");
        }

        public static readonly UnitConverter Singleton = new UnitConverter();
    }
}