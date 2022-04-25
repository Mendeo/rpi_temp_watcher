using System.Net.Mail;
using System.Net;

const int PAUSE_TIME = 30000;
const string RPI_TEMP_PROCESS = "vcgencmd";
const string RPI_TEMP_COMMAND = "measure_temp";
string EMAIL_SETTINGS_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.conf");

/*
FileStreamOptions fso = new FileStreamOptions();
fso.Share = FileShare.ReadWrite;
fso.Access = FileAccess.Read;
fso.Mode = FileMode.Open;
fso.Options = FileOptions.SequentialScan;
StreamReader sr = new StreamReader(fileToRead, fso);
*/

string[] emailData;
try
{
	StreamReader sr = new StreamReader(EMAIL_SETTINGS_FILE);
	emailData = sr.ReadToEnd().Split('\n');
	sr.Close();
}
catch (Exception ex)
{
	Console.WriteLine(ex.Message);
	return;
}
string? EMAIL = null;
string? PASSWORD = null;
string? SMTP_SERVER = null;
double MAX_TEMP = double.NaN;
double MIN_TEMP = double.NaN;

foreach (string line in emailData)
{
	string[] aux = line.Split('=');
	string test = aux[0].Trim().ToUpper();
	string data = aux[1].Replace("\r", String.Empty).Trim();
	if (test.Equals("EMAIL"))
	{
		EMAIL = data;
	}
	else if(test.Equals("PASSWORD"))
	{
		PASSWORD = data;
	}
	else if(test.Equals("SMTP_SERVER"))
	{
		SMTP_SERVER = data;
	}
	else if (test.Equals("MIN_TEMP"))
	{
		if (!double.TryParse(data, out MIN_TEMP)) throw new Exception("Error in cooldown temperature");
	}
	else if (test.Equals("MAX_TEMP"))
	{
		if (!double.TryParse(data, out MAX_TEMP)) throw new Exception("Error in alarm temperature");
	}
	if (EMAIL != null && PASSWORD != null && SMTP_SERVER != null && !double.IsNaN(MIN_TEMP) && !double.IsNaN(MAX_TEMP)) break;
}
if (EMAIL == null || PASSWORD == null || SMTP_SERVER == null || double.IsNaN(MIN_TEMP) || double.IsNaN(MAX_TEMP)) throw new Exception("Error in configuration file");

Console.WriteLine("EMAIL: " + EMAIL);
Console.WriteLine("Alarm CPU temperature is " + MAX_TEMP.ToString() + "°C");
Console.WriteLine("Cooldown CPU temperature is " + MIN_TEMP.ToString() + "°C");
bool alreadySent = false;
while(true)
{
	//string? rawData = sr.ReadLine();
	//sr.BaseStream.Position = 0;
	string? rawData = executeCommand(RPI_TEMP_PROCESS, RPI_TEMP_COMMAND);
	double? tC = convertTemp(rawData);
	if (tC is null)
	{
		Console.WriteLine("Can't get temperature.");
		Thread.Sleep(PAUSE_TIME);
		continue;
	}
	//Console.WriteLine(tC + "     ");
	//Console.SetCursorPosition(0, 0);
	if (tC >= MAX_TEMP)
	{
		if (!alreadySent)
		{
			sendEmail(tC);
			alreadySent = true;
		}
	}
	else if (alreadySent && tC <= MIN_TEMP)
	{
		alreadySent = false;
	}
	Thread.Sleep(PAUSE_TIME);
}

void sendEmail(double? tC)
{
	if (tC is null) return;
	const string SUBJECT = "Предупреждение о перегреве!";
	string BODY = "Температура процессора превысила " + MAX_TEMP.ToString() + "° и сейчас равна " + tC.ToString() + "°!";
	const bool NEED_SSL = true;

	SmtpClient client = new SmtpClient(SMTP_SERVER);
	client.EnableSsl = NEED_SSL;
	client.Credentials = new NetworkCredential(EMAIL, PASSWORD);
	MailMessage msg = new MailMessage(EMAIL, EMAIL, SUBJECT, BODY);
	msg.IsBodyHtml = false;
	try
	{
		client.Send(msg);
		Console.WriteLine(DateTime.Now.ToLongDateString() + "Сообщение отправлено успешно!");
	}
	catch(Exception ex)
	{
		Console.WriteLine(DateTime.Now.ToLongDateString() + " " + ex.Message);
	}
	finally
	{
		client.Dispose();
	}
}

string? executeCommand(string procName, string argsString)
{
	try
	{
		System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo(procName);
		// The following commands are needed to redirect the standard output.
		// This means that it will be redirected to the Process.StandardOutput StreamReader.
		procStartInfo.RedirectStandardOutput = true;
		procStartInfo.UseShellExecute = false;
		// Do not create the black window.
		procStartInfo.CreateNoWindow = true;
		procStartInfo.Arguments = argsString;
		// Now we create a process, assign its ProcessStartInfo and start it
		System.Diagnostics.Process proc = new System.Diagnostics.Process();
		proc.StartInfo = procStartInfo;
		proc.Start();
		// Get the output into a string
		return proc.StandardOutput.ReadToEnd();
	}
	catch (Exception ex)
	{
		Console.WriteLine(ex.Message);
		return null;
	}
}

double? convertTemp(string? rawData)
{
	if (rawData is null) return null;
	string aux = rawData.Split('=')[1];
	int index = aux.IndexOf('\'');
	if (index == -1) return null;
	string value = aux.Substring(0, index);
	double tC;
	if (!double.TryParse(value, out tC)) return null;
	return tC;
}