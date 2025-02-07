using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.CommandLine.Invocation;

using Csv;
using DotLiquid;
using Dumpify;

enum ConvertKinds
{
    SuministroFactEmitidas,
}

public static class Program
{
	private static IDictionary<string, string> _templates = new Dictionary<string, string>();

	private static string GetTemplate(string name)
	{
		if (!_templates.ContainsKey(name))
		{
			var assembly = Assembly.GetExecutingAssembly();
			var result = assembly.GetManifestResourceNames()
				.Where(r => r.EndsWith(name))
				.Select(r => assembly.GetManifestResourceStream(r))
				.FirstOrDefault();
			if (result == null)
			{
				throw new Exception($"Resource '{name}' not found.");
			}

			using var reader = new StreamReader(result);
			_templates[name] = reader.ReadToEnd();
		}
		return _templates[name];
	}

	private static string CreateRegistroLRFacturasEmitidas(IEnumerable<ICsvLine> reader)
	{
		var sb = new StringBuilder(4096);
		var template = Template.Parse(GetTemplate("RegistroLRFacturasEmitidas.template.xml"));
		
		foreach (var row in reader)
		{
			var fields = row.Headers.ToDictionary(x => x, x => (object)row[x]);
			$"Processing row: {row.Index -1} => ...".Dump(typeRenderingConfig: new TypeRenderingConfig { QuoteStringValues = false });
			fields.Dump(typeNames: new TypeNamingConfig { ShowTypeNames = false });
			sb.Append(template.Render(Hash.FromDictionary(fields)));
		}

		return sb.ToString();
	}

	private static string ConvertSuministroFactEmitidas(IEnumerable<ICsvLine> reader)
	{
		var sb = new StringBuilder(4096);
		var template = Template.Parse(GetTemplate("SuministroFactEmitidas.template.xml"));
		var invoices = CreateRegistroLRFacturasEmitidas(reader);

		sb.Append(template.Render(Hash.FromAnonymousObject(new {
			INVOICES = invoices
		})));

		return sb.ToString();
	
	}

	private static void Convert(ConvertKinds kind, FileInfo input, FileInfo output, char separator)
	{
		//using var reader = Sep.New(separator).Reader().FromFile(input.FullName);
		string result;
		var csv = CsvReader.ReadFromStream(input.OpenRead(), new CsvOptions {
			Separator = separator,
			SkipRow = (row, idx) => row.IsEmpty || row.Length == 0 || row.Span[0] == '#',
			TrimData = true,
			HeaderMode = HeaderMode.HeaderPresent
		});

		switch (kind)
		{
			case ConvertKinds.SuministroFactEmitidas:
				result = ConvertSuministroFactEmitidas(csv);
				break;
			default:
				throw new NotImplementedException();
		}

		using var reader = new StringReader(result);
		using var ostream = output.Open(FileMode.Create, FileAccess.Write);
		using var writer = new StreamWriter(ostream);
		int ch;
		
		while ((ch = reader.Read()) != -1)
		{
			var c = (char)ch;
			if (c == '&')
			{
				writer.Write("&amp;");
				continue;
			}
			// Replace non-ASCII characters with their XML numeric entity.
			if (c > 127)
			{
				writer.Write($"&#x{ch:X};");
				continue;
			}
			writer.Write((char)ch);
		}

		writer.Flush();
	}

	public static int Main(string[] args)
	{
		var version = Assembly.GetExecutingAssembly().GetName().Version;
		var commands = new RootCommand($"AEAT Crappy utility v{version}.");
		var convertCommand = new Command("convert", "Converts CSV file to AEAT SII's SOAP/XML formats.");
		var convertKindOption = 
		    new Option<ConvertKinds>(	"--kind", 	description: "Type of CSV=>SOAP/XML conversion to perform")
		    {
			    IsRequired = true
		    };
		var convertInputOption =
		    new Option<FileInfo>(	"--input",	description: "Input CSV file")
		    {
			    IsRequired = true,
		    };
		var convertOutputOption =
		    new Option<FileInfo>(	"--output",	description: "Output XML file")
		    {
				IsRequired = true
		    };
		var convertSeparatorOption =
		    new Option<char>(		"--separator",	description: "Separator used by CSV file", getDefaultValue: () => ';');
		var convertOutputOverwriteOption =
		    new Option<bool>(		"--overwrite",	description: "Overwrite output file if it exists", getDefaultValue: () => false);
		convertInputOption.AddValidator(result => {
			var file = result.GetValueForOption(convertInputOption);
			if (file?.Exists == false)
			{
				result.ErrorMessage = $"File '{file.FullName}' missing?!";
			}
		});
		convertOutputOption.AddValidator(result => {
			var file = result.GetValueForOption(convertOutputOption);
			var force = result.GetValueForOption(convertOutputOverwriteOption);
			if (file?.Exists == true && !force)
			{
				result.ErrorMessage = $"File '{file.FullName}' already exists?!";
			}
		});

		convertCommand.AddOption(convertKindOption);
		convertCommand.AddOption(convertInputOption);
		convertCommand.AddOption(convertOutputOption);
		convertCommand.AddOption(convertSeparatorOption);
		convertCommand.AddOption(convertOutputOverwriteOption);
		convertCommand.SetHandler(Convert, 
			convertKindOption,
			convertInputOption,
			convertOutputOption,
			convertSeparatorOption
		);
		commands.Add(convertCommand);

		var executor = new CommandLineBuilder(commands)
			.UseHelp()
			.UseVersionOption()
			.UseParseErrorReporting()
			.UseExceptionHandler()
			.Build();
	
		return executor.Invoke(args);
	}

}

// vim: set ft=cs ts=4 sw=4 noet ai:
