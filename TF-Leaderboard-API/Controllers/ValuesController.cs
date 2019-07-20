using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Data;
using System.Data.OleDb;
using System.IO;

namespace TF_Leaderboard_API.Controllers
{
	[RoutePrefix("tf-leaderboard")]
	public class ValuesController : ApiController
	{
		private string dataUrl;
		private string caseDbFileName;
		private string templateDbFileName;
		private Template curTemplate;

		public ValuesController()
		{
			dataUrl = @"C:\Dashboard\publish\Data\";
			caseDbFileName = "caseData.mdb";
			templateDbFileName = "templateData.mdb";
			curTemplate = null;
		}

		[Route("template/{id}")]
		public object GetTemplate(int id)
		{
			ReadTemplateSettings(id);

			return this.curTemplate.ToData();
		}

		[Route("casedata")]
		public Dictionary<string, object> GetCaseData(DateTime startDate, DateTime endDate)
		{
			Dictionary<string, Member> result = ReadCaseDataFromAccessDB(startDate, endDate);
			Dictionary<string, object> output = new Dictionary<string, object>();

			result.Keys.ToList().ForEach(key =>
			{
				output[key] = result[key].ToData();
			});

			return output;
		}

		[Route("getconfiguration")]
		public Dictionary<string, string> GetConfiguration()
		{
			return GetConfigurationFromAccess();
		}

		[Route("saveconfiguration")]
		[HttpPost]
		public bool SavConfiguration([FromBody] Dictionary<string, string> settings)
		{
			return SaveConfigurationToAccess(settings);
		}

		[Route("getimages/{id}")]
		public object GetImagesForTemplate(int id)
		{
			return readTemplateImages(id);
		}

		[Route("test")]
		public string TestAPI()
		{
			return "Test message.";
		}

		/// <summary>
		/// Read template setting by its id.
		/// </summary>
		/// <param name="templateID"></param>
		/// <returns></returns>
		private Template ReadTemplateSettings(int templateID)
		{
			string connectStr = String.Format("Provider=Microsoft.Jet.OLEDB.4.0;Data Source={0}{1}", dataUrl, templateDbFileName);
			string sqlStr = string.Format("select * from TemplateSettings where [Template.ID] = {0}", templateID);

			Template template = null;
			Team curTeam = null;

			using (OleDbConnection connect = new OleDbConnection(connectStr))
			{
				OleDbCommand command = new OleDbCommand(sqlStr, connect);

				try
				{
					connect.Open();

					using (OleDbDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							if (template == null)
							{
								template = new Template()
								{
									ID = templateID,
									Name = reader["TemplateName"].ToString(),
									IconPath = reader["IconFileName"].ToString(),
									Teams = new List<Team>()
								};
							}

							int teamID = Convert.ToInt32(reader["Team.ID"].ToString());

							if (curTeam == null || curTeam.ID != teamID)
							{
								curTeam = template.Teams.FirstOrDefault(o => o.ID == teamID);

								if (curTeam == null)
								{
									curTeam = new Team()
									{
										ID = teamID,
										Name = reader["TeamName"].ToString(),
										LogoPath = reader["LogoFileName"].ToString(),
										Members = new List<Member>()
									};

									template.Teams.Add(curTeam);
								}
							}

							curTeam.Members.Add(new Member()
							{
								ID = reader["MemberID"].ToString(),
								Name = reader["MemberName"].ToString(),
								CaseNumber = 0,
								TotalPoint = 0
							});
						}

						connect.Close();
					}
				}
				catch (Exception ex)
				{
					WriteToLog(ex.Message);
					throw ex;
				}
			}

			curTemplate = template;

			return template;
		}

		/// <summary>
		/// Read case data from access db file.
		/// </summary>
		private Dictionary<string, Member> ReadCaseDataFromAccessDB(DateTime startDate, DateTime endDate)
		{
			string connectionString = String.Format("Provider=Microsoft.Jet.OLEDB.4.0;Data Source={0}{1}", dataUrl, caseDbFileName); ;
			string strSQL = string.Format("SELECT ownerid, owneridname, ownerteam, subjectidname, new_resolvedon, productidname, points FROM CASES " +
				"WHERE DateValue(new_resolvedon) >= DateValue('{0}') AND DateValue(new_resolvedon) <= DateValue('{1}')", startDate.ToShortDateString(), endDate.ToShortDateString());

			Dictionary<string, Member> memberCases = new Dictionary<string, Member>();

			using (OleDbConnection connection = new OleDbConnection(connectionString))
			{
				OleDbCommand command = new OleDbCommand(strSQL, connection);

				try
				{
					connection.Open();

					using (OleDbDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							try
							{
								string ownerID = reader["ownerid"].ToString();
								double casePoint = string.IsNullOrEmpty(reader["points"].ToString()) ? 0 : Convert.ToDouble(reader["points"].ToString());

								if (!memberCases.ContainsKey(ownerID))
								{
									memberCases[ownerID] = new Member()
									{
										CaseNumber = 0,
										TotalPoint = 0
									};
								}

								memberCases[ownerID].CaseNumber++;
								memberCases[ownerID].TotalPoint += casePoint;
							}
							catch (Exception readException)
							{
								WriteToLog(readException.Message);
								throw readException;
							}
						}
					}
				}
				catch (Exception ex)
				{
					WriteToLog(ex.Message);
					throw ex;
				}
			}

			return memberCases;
		}

		private object readTemplateImages(int templateID)
		{
			string connectStr = String.Format("Provider=Microsoft.Jet.OLEDB.4.0;Data Source={0}{1}", dataUrl, templateDbFileName);

			string templateIcon = string.Empty;
			Dictionary<string, string> teamLogos = new Dictionary<string, string>();

			using (OleDbConnection connect = new OleDbConnection(connectStr))
			{
				try
				{
					connect.Open();

					string commandText1 = string.Format("select IconFileName from Template where [ID] = {0}", templateID);
					OleDbCommand cmd1 = new OleDbCommand(commandText1, connect);

					using (OleDbDataReader reader1 = cmd1.ExecuteReader())
					{
						reader1.Read();
						string iconFileName = reader1["IconFileName"].ToString();
						templateIcon = ConvertImageToBase64("templates/" + iconFileName);
					}

					string commandText2 = string.Format("SELECT TeamName, LogoFileName FROM Team where TemplateID = {0}", templateID);
					OleDbCommand cmd2 = new OleDbCommand(commandText2, connect);

					using (OleDbDataReader reader2 = cmd2.ExecuteReader())
					{
						while (reader2.Read())
						{
							string teamName = reader2["TeamName"].ToString();
							string logoFileName = reader2["LogoFileName"].ToString();

							teamLogos[teamName] = ConvertImageToBase64("teams/" + logoFileName);
						}
					}

					connect.Close();
				}
				catch (Exception ex)
				{
					WriteToLog(ex.Message);
					throw ex;
				}
			}

			return new
			{
				icon = templateIcon,
				teams = teamLogos
			};
		}

		private Dictionary<string, string> GetConfigurationFromAccess()
		{
			string connectStr = String.Format("Provider=Microsoft.Jet.OLEDB.4.0;Data Source={0}{1}", dataUrl, templateDbFileName);
			string sqlStr = string.Format("select Key, Value from Configuration");

			Dictionary<string, string> configuration = new Dictionary<string, string>();

			using (OleDbConnection connect = new OleDbConnection(connectStr))
			{
				OleDbCommand command = new OleDbCommand(sqlStr, connect);

				try
				{
					connect.Open();

					using (OleDbDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							configuration.Add(reader["Key"].ToString(), reader["Value"].ToString());
						}

						connect.Close();
					}
				}
				catch (Exception ex)
				{
					WriteToLog(ex.Message);
					throw ex;
				}
			}

			return configuration;
		}

		private bool SaveConfigurationToAccess(Dictionary<string, string> settings)
		{
			string connectStr = String.Format("Provider=Microsoft.Jet.OLEDB.4.0;Data Source={0}{1}", dataUrl, templateDbFileName);

			using (OleDbConnection connect = new OleDbConnection(connectStr))
			{
				try
				{
					connect.Open();

					OleDbCommand command = new OleDbCommand();
					command.Connection = connect;
					command.CommandType = CommandType.Text;

					foreach (string key in settings.Keys)
					{
						command.CommandText = string.Format("update Configuration set [Value] = '{0}' where [Key] = '{1}';", settings[key], key);
						command.ExecuteNonQuery();
					}

					connect.Close();
				}
				catch (Exception ex)
				{
					WriteToLog(ex.Message);
					return false;
				}
			}

			return true;
		}

		private bool WriteToLog(string msg)
		{
			try
			{
				FileStream fs = new FileStream("C:\\Dashboard\\publish\\Log\\Log.txt", FileMode.Append, FileAccess.Write);
				StreamWriter sw = new StreamWriter((Stream)fs);
				sw.WriteLine(msg);
				sw.Close();
				fs.Close();
				return true;
			}
			catch (Exception ex)
			{
				throw ex;
			}
		}

		private string ConvertImageToBase64(string path)
		{
			string fullPath = string.Format("{0}images/{1}", this.dataUrl, path);

			return Convert.ToBase64String(File.ReadAllBytes(fullPath));
		}
	}


	public class Template
	{
		public int ID { get; set; }
		public string Name { get; set; }
		public string IconPath { get; set; }
		public List<Team> Teams { get; set; }

		public object ToData()
		{
			return new
			{
				name = Name,
				//icon = Icon,
				teams = Teams.Select(o => o.ToData()),
			};
		}
	}

	public class Member
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public int CaseNumber { get; set; }
		public double TotalPoint { get; set; }
		public object ToData()
		{
			return new
			{
				id = ID,
				name = Name,
				case_number = CaseNumber,
				total_point = TotalPoint
			};
		}
	}

	public class Team
	{
		public int ID { get; set; }
		public string Name { get; set; }
		public string LogoPath { get; set; }
		public List<Member> Members { get; set; }

		public object ToData()
		{
			return new
			{
				name = Name,
				//logo = Logo,
				members = Members.Select(o => o.ToData())
			};
		}
	}
}
