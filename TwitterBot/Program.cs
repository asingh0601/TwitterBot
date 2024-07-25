using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using System;
using System.Data.SqlClient;

namespace TwitterBot
{
	internal class Program
	{
		private static readonly string ConnectionString;

		static Program()
		{
			ConnectionString = $"Data Source={Configuration.Server}{(string.IsNullOrWhiteSpace(Configuration.Port) ? string.Empty : $":{Configuration.Port}")};Initial Catalog={Configuration.DbName};Persist Security Info=True;User ID={Configuration.DbUserId};Password={Configuration.DbUserPassword};TrustServerCertificate=True";
		}

		#region variables
		private const string TwitterLoginUrl = "https://x.com/i/flow/login";
		private static List<Bot>? Bots;
		private static bool AnonymousMode = false;
		private static string? TwitterTargetUrl;
		private static int SpaceJoinIntervalOffset = 0;
		private static string? CommandUserName;
		private static INetwork? NetworkInterceptor;
		private static readonly string MSG_IDENTIFIER = "BOT_RESPONSE: ";
		#endregion

		static void Main(string[] args)
		{
			TwitterTargetUrl = args[1];
			var taskType = args[0];
			var otherCommands = args.Skip(2).ToArray();
			foreach (var command in otherCommands)
			{
				if (command.Contains("-anonymous"))
				{
					AnonymousMode = true;
				}

				if (command.Contains("-timeoffset"))
				{
					_ = int.TryParse(command.Split(":").LastOrDefault(), out SpaceJoinIntervalOffset);
				}
				if (command.Contains("-commandBy"))
				{
					CommandUserName = command.Split("#").LastOrDefault();
				}
			}
			Bots = GetBotsFromDb();
			var timeStampString = $"{DateTime.Now:yyyyMMddHHmmssffff}";

			Action<Bot>? function = null;
			switch (taskType)
			{
				case "/likeretweet":
					function = LikeRetweetTweetsBot;
					break;
				case "/join":
					function = JoinTwitterSpace;
					break;
				case "/joinlaugh":
					function = JoinTwitterSpaceAndLaugh;
					break;
				case "/rjoin":
					function = RunTwitterSpacesBot;
					break;
				case "/follow":
					function = FollowBot;
					break;
				case "/reportspace":
					function = ReportTwitterSpace;
					break;
			}
			var tasks = new List<Task>();
			foreach (var bot in Bots)
			{
				var masterDirPath = @$"C:\TwitterBotChromeProfiles\master\{bot.TwitterUserName}";
				var rootPath = $@"C:\TwitterBotChromeProfiles\{timeStampString}";
				bot.UserDataDirectory = @$"{rootPath}\{bot.TwitterUserName}";
				if (Directory.Exists(masterDirPath))
				{
					bot.MasterUserDataExists = true;
					Directory.CreateDirectory(rootPath);
					Directory.CreateDirectory(bot.UserDataDirectory);
					CopyFilesRecursively(masterDirPath, bot.UserDataDirectory);
				}
				else
				{
					bot.MasterUserDataExists = false;
				}

				if (function is not null)
				{
					tasks.Add(Task.Factory.StartNew(() => function(bot)));
				}
			}
			Task.WaitAll([.. tasks]);
		}

		#region BotLogin
		private static bool LoginToTwitter(ChromeDriver driver, Bot bot)
		{
			var profileIconLocator = By.XPath(@"//a[@data-testid=""AppTabBar_Profile_Link""]");
			try
			{
				driver.Navigate().GoToUrl($"https://x.com/home");
				try
				{
					WaitUntilElementClickable(driver, profileIconLocator);
					Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} has logged in to twitter.");
					bot.LoginSuccessful = true;
					return true;
				}
				catch
				{
					if (!CheckAndMarkIdSuspended(driver, bot) || !CheckAndMarkIdLocked(driver, bot) || !CheckAndMarkEmailVerification(driver, bot))
					{
						bot.LoginSuccessful = false;
						SafelyExitBotInstance(driver, bot);
						return false;
					}
					throw;
				}
			}
			catch
			{
				try
				{
					var userNameFieldLocator = By.Name("text");
					var passwordFieldLocator = By.Name("password");
					driver.Navigate().GoToUrl(TwitterLoginUrl);
					WaitUntilElementClickable(driver, userNameFieldLocator);
					var usernameField = driver.FindElement(userNameFieldLocator);
					usernameField.SendKeys(bot.TwitterUserName);
					usernameField.SendKeys(Keys.Enter);
					try
					{
						var emailFieldLocator = By.Name("text");
						WaitUntilElementClickable(driver, emailFieldLocator);
						var emailField = driver.FindElement(emailFieldLocator);
						emailField.SendKeys(bot.TwitterEmail);
						emailField.SendKeys(Keys.Enter);
					}
					catch (Exception) { }

					WaitUntilElementClickable(driver, passwordFieldLocator);
					var passwordField = driver.FindElement(passwordFieldLocator);
					passwordField.SendKeys(bot.TwitterPassword);
					passwordField.SendKeys(Keys.Enter);
					try
					{
						var emailFieldLocator = By.Name("text");
						WaitUntilElementClickable(driver, emailFieldLocator);
						var emailField = driver.FindElement(emailFieldLocator);
						emailField.SendKeys(bot.TwitterEmail);
						emailField.SendKeys(Keys.Enter);
					}
					catch (Exception) { }

					try
					{
						var alreadyLoggedInSpanLocator = By.XPath("//span[text()='The account being added is already logged in.']");
						WaitUntilElementVisible(driver, alreadyLoggedInSpanLocator);
						var nextButtonLocator = By.XPath("//span[text()='Next']");
						WaitUntilElementClickable(driver, nextButtonLocator);
						var nextButtonField = driver.FindElement(nextButtonLocator);
						nextButtonField.Click();
					}
					catch (Exception) { }
					if (!CheckAndMarkIdSuspended(driver, bot) || !CheckAndMarkIdLocked(driver, bot) || !CheckAndMarkEmailVerification(driver, bot))
					{
						SafelyExitBotInstance(driver, bot);
						return false;
					}
					WaitUntilElementClickable(driver, profileIconLocator);
					Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} has logged in to twitter.");
					bot.LoginSuccessful = true;
					return true;
				}
				catch (Exception)
				{
					Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} could not log in to twitter.");
					MarkLoginFailure(bot);
					SafelyExitBotInstance(driver, bot);
					bot.LoginSuccessful = false;
					return false;
				}
			}
		}
		#endregion

		#region botactions
		private static void LikeRetweetTweetsBot(Bot bot)
		{
			var driver = GetChromeDriver(bot);
			try
			{
				if (LoginToTwitter(driver, bot))
				{
					driver.Navigate().GoToUrl(TwitterTargetUrl);
					try
					{
						var likeButtonLocator = By.XPath(@"(//button[@data-testid=""like""])[1]");
						WaitUntilElementClickable(driver, likeButtonLocator);
						var jse = (IJavaScriptExecutor)driver;
						jse.ExecuteScript("window.scrollBy(0,250)");
						Thread.Sleep(1000);
						jse.ExecuteScript("window.scrollBy(0,-240)");
						Thread.Sleep(new Random().Next(1000, 240000));
						var likeButton = driver.FindElement(likeButtonLocator);
						likeButton.Click();
						Thread.Sleep(1000);

						var retweetButtonLocator = By.XPath(@"(//button[@data-testid=""retweet""])[1]");
						var repostOptionLocator = By.XPath(@"//span[text()='Repost']");
						WaitUntilElementClickable(driver, retweetButtonLocator);
						var retweetButton = driver.FindElement(retweetButtonLocator);
						retweetButton.Click();
						Thread.Sleep(1000);
						WaitUntilElementClickable(driver, repostOptionLocator);
						var repostButton = driver.FindElement(repostOptionLocator);
						repostButton.Click();
						Thread.Sleep(1000);

						jse.ExecuteScript("window.scrollBy(0,350)");
						Thread.Sleep(1000);
						jse.ExecuteScript("window.scrollBy(0,-280)");
						Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} has liked & retweeted the target tweet.");
						Thread.Sleep(3000);
					}
					catch (Exception) { }
				}
				else
				{
					Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} could not like & retweet target tweet.");
				}
			}
			finally
			{
				SafelyExitBotInstance(driver, bot);
			}
		}
		private static void FollowBot(Bot bot)
		{
			var driver = GetChromeDriver(bot);
			try
			{
				Thread.Sleep(new Random().Next(1000, 600000));
				if (LoginToTwitter(driver, bot))
				{
					driver.Navigate().GoToUrl(TwitterTargetUrl);
					try
					{
						var followButtonLocator = By.XPath(@"//span[text()='Follow']");
						WaitUntilElementClickable(driver, followButtonLocator);
						var followButton = driver.FindElement(followButtonLocator);
						followButton.Click();
						Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} has followed the target.");
					}
					catch (Exception) { }
				}
				else
				{
					Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} could not follow target.");
				}
			}
			finally
			{
				SafelyExitBotInstance(driver, bot);
			}
		}
		private static void RunTwitterSpacesBot(Bot bot)
		{
			var driver = GetChromeDriver(bot);
			try
			{
				if (LoginToTwitter(driver, bot))
				{
					Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} has logged in to twitter.");
					for (int i = 0; i < 1000; i++)
					{
						if (JoinTwitterSpace(bot, driver))
						{
							Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} has joined twitter space.");
							Thread.Sleep(new Random().Next(120000, 240000) + (SpaceJoinIntervalOffset * 1000));
						}
					}

				}
			}
			finally
			{
				SafelyExitBotInstance(driver, bot);
			}
		}
		private static void JoinTwitterSpace(Bot bot)
		{
			ChromeDriver driver = GetChromeDriver(bot);
			JoinTwitterSpace(bot, driver);
			Task.Delay(15 * 60 * 1000).ContinueWith((task) =>
			{
				SafelyExitBotInstance(driver, bot);
			});
		}
		private static void ReportTwitterSpace(Bot bot)
		{
			ChromeDriver driver = GetChromeDriver(bot);
			JoinTwitterSpace(bot, driver);
			var moreOptionsLocator = By.XPath("/html/body/div[1]/div/div/div[1]/div/div[1]/div/div/div/div[1]/div/div/div[1]/div[1]/div/button[3]");
			var reportSpaceLocator = By.XPath("/html/body/div[1]/div/div/div[1]/div[2]/div/div/div/div[2]/div/div[3]/div/div/div/div[2]");
			var violenceOptionLocator = By.XPath("/html/body/div[1]/div/div/div[1]/div/div[1]/div/div/div/div[3]/div[2]/div/div/div/div/div/div[4]");
			var leaveButtonLocator = By.XPath(@"//span[text()='Leave']");

			try
			{
				var rnd = new Random();
				WaitUntilElementClickable(driver, moreOptionsLocator);
				var moreOptions = driver.FindElement(moreOptionsLocator);
				moreOptions.Click();
				Thread.Sleep(rnd.Next(2000, 4500));

				WaitUntilElementClickable(driver, reportSpaceLocator);
				var reportSpace = driver.FindElement(reportSpaceLocator);
				reportSpace.Click();
				Thread.Sleep(rnd.Next(2000, 4500));

				WaitUntilElementClickable(driver, violenceOptionLocator);
				var violenceOption = driver.FindElement(violenceOptionLocator);
				violenceOption.Click();
				Thread.Sleep(rnd.Next(2000, 4500));

				WaitUntilElementClickable(driver, leaveButtonLocator);
				var leaveButton = driver.FindElement(leaveButtonLocator);
				Thread.Sleep(rnd.Next(2000, 4500));
			}
			finally
			{
				SafelyExitBotInstance(driver, bot);
			}
		}
		private static void JoinTwitterSpaceAndLaugh(Bot bot)
		{
			ChromeDriver driver = GetChromeDriver(bot);
			if (JoinTwitterSpace(bot, driver))
			{
				ShowLaughEmoji(driver);
			}

			Task.Delay(15 * 60 * 1000).ContinueWith((task) =>
			{
				SafelyExitBotInstance(driver, bot);
			});
		}
		private static bool JoinTwitterSpace(Bot bot, ChromeDriver driver)
		{
			try
			{
				if (LoginToTwitter(driver, bot))
				{
					driver.Navigate().GoToUrl(TwitterTargetUrl);
					var startListeningButtonLocator = AnonymousMode ? By.XPath(@"//span[text()='Start listening anonymously']") : By.XPath(@"//span[text()='Start listening']");
					var anonymousToggleLocator = By.XPath(@"//input[@type='checkbox']");
					if (AnonymousMode)
					{
						WaitUntilElementClickable(driver, anonymousToggleLocator);
						var anonymousButton = driver.FindElement(anonymousToggleLocator);
						anonymousButton.Click();
					}
					WaitUntilElementClickable(driver, startListeningButtonLocator);
					var startListeningButton = driver.FindElement(startListeningButtonLocator);
					startListeningButton.Click();
					var gotItButtonLocator = By.XPath(@"//span[text()='Got it']");
					WaitUntilElementClickable(driver, gotItButtonLocator);
					var gotItButton = driver.FindElement(gotItButtonLocator);
					gotItButton.Click();
					UpdateSpaceUrlToProcessEntry(bot);
					return true;
				}
			}
			catch (Exception)
			{
				try
				{
					var leaveButtonLocator = By.XPath(@"//span[text()='Leave']");
					UpdateSpaceUrlToProcessEntry(bot);
					WaitUntilElementClickable(driver, leaveButtonLocator);
					return true;
				}
				catch (Exception)
				{
					Console.WriteLine($"{MSG_IDENTIFIER}{bot.TwitterUserName} could not join twitter space.");
				}
			}
			return false;
		}
		private static void ShowLaughEmoji(ChromeDriver driver)
		{
			var emojis = new List<string> {
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[1]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[2]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[3]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[4]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[5]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[6]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[7]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[8]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[9]",
				"//*[@id=\"layers\"]/div[2]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[10]"
			};
			for (int i = 0; i < 1000; i++)
			{
				var emojisTogglerLocator = By.XPath(@"//*[@id=""layers""]/div/div[1]/div/div/div/div[2]/div/div/div[2]/div/button");
				WaitUntilElementClickable(driver, emojisTogglerLocator);
				var emojisToggler = driver.FindElement(emojisTogglerLocator);
				emojisToggler.Click();

				var laughEmojiLocator = By.XPath(emojis[0]);
				WaitUntilElementClickable(driver, laughEmojiLocator);
				var laughEmoji = driver.FindElement(laughEmojiLocator);
				laughEmoji.Click();
				Thread.Sleep(1000);
			}
		}
		#endregion

		#region helpermethods
		private static void SaveProcessId(Bot bot)
		{
#if !DEBUG
			try
			{
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"INSERT INTO [dbo].[SpaceProcessIds] ([ProcessDate],[CommandUserName],[UserName],[LoginSuccessful],[Directory],[Url],[ProcessId],[ProcessKilled]) VALUES ('{DateTime.Now:yyyy-MM-dd HH:mm:ss}','{CommandUserName}','{bot.TwitterUserName}',0,'{bot.UserDataDirectory}','{TwitterTargetUrl}',{bot.ProcessId},0)";

				using SqlCommand command = new(sqlQuery, conn);
				var result = command.ExecuteNonQuery();
				conn.Close();
			}
			catch (Exception) { }
#endif
		}
		private static void UpdateSpaceUrlToProcessEntry(Bot bot)
		{
#if !DEBUG
			try
			{
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"UPDATE [dbo].[SpaceProcessIds] SET Url = '{TwitterTargetUrl}', LoginSuccessful = 1 Where ProcessId = '{bot.ProcessId}' AND UserName = '{bot.TwitterUserName}'";

				using SqlCommand command = new(sqlQuery, conn);
				var result = command.ExecuteNonQuery();
				conn.Close();
			}
			catch (Exception) { }
#endif
		}
		private static void MarkLoginFailure(Bot bot)
		{
			try
			{
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"Update BotDetails SET LoginFailure = LoginFailure + 1 WHERE UserName = '{bot.TwitterUserName}'";

				using SqlCommand command = new(sqlQuery, conn);
				var result = command.ExecuteNonQuery();
				conn.Close();
			}
			catch (Exception) { }
		}
		private static bool CheckAndMarkEmailVerification(ChromeDriver driver, Bot bot)
		{
			try
			{
				var emailVerifyDivLocator = By.XPath("//div[normalize-space()='Please verify your email address.']");
				WaitUntilElementVisible(driver, emailVerifyDivLocator);
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"Update BotDetails SET IdLocked = 1 WHERE UserName = '{bot.TwitterUserName}'";

				using SqlCommand command = new(sqlQuery, conn);
				var result = command.ExecuteNonQuery();
				conn.Close();
				return false;
			}
			catch (Exception) { return true; }
		}
		private static bool CheckAndMarkIdSuspended(ChromeDriver driver, Bot bot)
		{
			try
			{
				var idSuspendedSpanLocator = By.XPath("//span[text()='Your account is suspended']");
				WaitUntilElementVisible(driver, idSuspendedSpanLocator);
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"Update BotDetails SET IdSuspended = 1 WHERE UserName = '{bot.TwitterUserName}'";

				using SqlCommand command = new(sqlQuery, conn);
				var result = command.ExecuteNonQuery();
				conn.Close();
				return false;
			}
			catch (Exception) { return true; }
		}
		private static bool CheckAndMarkIdLocked(ChromeDriver driver, Bot bot)
		{
			bool idLocked = false;
			try
			{
				try
				{
					var verificationCodeDivLocator = By.XPath(@"//div[normalize-space()='We sent your verification code.']");
					WaitUntilElementVisible(driver, verificationCodeDivLocator);
					idLocked = true;
				}
				catch
				{
					try
					{
						var idLockedDivLocator = By.XPath(@"//div[normalize-space()='Your account has been locked.']");
						WaitUntilElementVisible(driver, idLockedDivLocator);
						idLocked = true;
					}
					catch { }
				}
				if (idLocked)
				{
					SqlConnection conn = new(ConnectionString);
					conn.Open();
					var sqlQuery = $"Update BotDetails SET IdLocked = 1 WHERE UserName = '{bot.TwitterUserName}'";

					using SqlCommand command = new(sqlQuery, conn);
					var result = command.ExecuteNonQuery();
					conn.Close();
					return false;
				}
				return true;
			}
			catch (Exception) { return true; }
		}
		private static List<Bot> GetBotsFromDb()
		{
			string preciseSelector = "DISTINCT";
#if DEBUG
			preciseSelector = "DISTINCT";
#endif
			List<Bot> bots = [];
			try
			{
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"SELECT {preciseSelector} UserName, EmailId, Password FROM BotDetails WHERE LoginFailure < 3 AND IdDisabled = 0 AND IdSuspended = 0 AND IdLocked = 0";

				using SqlCommand command = new(sqlQuery, conn);
				var result = command.ExecuteReader();

				while (result.Read())
				{
					bots.Add(new Bot { TwitterUserName = result["UserName"].ToString(), TwitterEmail = result["EmailId"].ToString(), TwitterPassword = result["Password"].ToString() });
				}
				conn.Close();
			}
			catch (Exception) { }
			return bots;
		}
		private static ChromeDriver GetChromeDriver(Bot bot)
		{
#if !DEBUG
			Proxy proxy = new()
			{
				Kind = ProxyKind.Manual,
				IsAutoDetect = false,
				SslProxy = $"{Configuration.ProxyIp}:{Configuration.ProxyPort}",
				HttpProxy = $"{Configuration.ProxyIp}:{Configuration.ProxyPort}"
			};
#endif
			var svc = ChromeDriverService.CreateDefaultService();
			var chromeOptions = new ChromeOptions
			{
#if !DEBUG
				Proxy = proxy
#endif
			};
			chromeOptions.AddArguments(new List<string>()
			{
#if !DEBUG
				"--headless=new",
#endif
				"no-sandbox",
				"start-maximized",
				"disable-notifications",
				"disable-web-security",
				"ignore-certificate-errors",
#if !DEBUG
				"--blink-settings=imagesEnabled=false",
#endif
				"--disable-blink-features=AutomationControlled",
				@$"--user-data-dir={bot.UserDataDirectory}",
			});

			chromeOptions.AddExcludedArgument("enable-automation");
			chromeOptions.AddAdditionalChromeOption("useAutomationExtension", false);

			var driver = new ChromeDriver(svc, chromeOptions);
			driver.ExecuteScript("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");
#if !DEBUG
			NetworkAuthenticationHandler handler = new()
			{
				UriMatcher = d => true, //d.Host.Contains("your-host.com")
				Credentials = new PasswordCredentials(Configuration.ProxyUserName, Configuration.ProxyPassword)
			};

			NetworkInterceptor = driver.Manage().Network;
			NetworkInterceptor.AddAuthenticationHandler(handler);
			NetworkInterceptor.StartMonitoring();
#endif
			bot.ProcessId = svc.ProcessId;
			SaveProcessId(bot);
			return driver;
		}
		private static IWebElement WaitUntilElementClickable(ChromeDriver driver, By elementLocator, int timeout = 10)
		{
			try
			{
				var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeout));
				return wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementToBeClickable(elementLocator));
			}
			catch (NoSuchElementException)
			{
				Console.WriteLine($"{MSG_IDENTIFIER}Element with locator: '{elementLocator}' was not found in current context page.");
				throw;
			}
		}
		private static IWebElement WaitUntilElementVisible(ChromeDriver driver, By elementLocator, int timeout = 10)
		{
			try
			{
				var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeout));
				return wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementIsVisible(elementLocator));
			}
			catch (NoSuchElementException)
			{
				Console.WriteLine($"{MSG_IDENTIFIER}Element with locator: '{elementLocator}' was not found in current context page.");
				throw;
			}
		}

		private static void CopyFilesRecursively(string sourcePath, string targetPath)
		{
			foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
			{
				Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
			}

			foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
			{
				FileCopy(newPath, newPath.Replace(sourcePath, targetPath));
			}
		}

		private static void FileCopy(string oldPath, string newPath)
		{
			FileStream input = null;
			FileStream output = null;
			try
			{
				input = new FileStream(oldPath, FileMode.Open);
				output = new FileStream(newPath, FileMode.Create, FileAccess.ReadWrite);

				byte[] buffer = new byte[32768];
				int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					output.Write(buffer, 0, read);
				}
			}
			catch (Exception e)
			{
			}
			finally
			{
				input.Close();
				input.Dispose();
				output.Close();
				output.Dispose();
			}
		}

		private static void SafelyExitBotInstance(ChromeDriver driver, Bot bot)
		{
			if (!bot.MasterUserDataExists && bot.LoginSuccessful)
			{
				if (Directory.Exists(bot.UserDataDirectory))
				{
					Directory.CreateDirectory(@$"C:\TwitterBotChromeProfiles\master\{bot.TwitterUserName}");
					CopyFilesRecursively(bot.UserDataDirectory, @$"C:\TwitterBotChromeProfiles\master\{bot.TwitterUserName}");
				}
			}
			if (Directory.Exists(bot.UserDataDirectory))
			{
				var dir = new DirectoryInfo(bot.UserDataDirectory);
				try
				{
					dir.Delete(true);
					dir.Parent?.Delete();
				}
				catch { }
			}
			try
			{
				NetworkInterceptor?.StopMonitoring();
				driver.Close();
				driver.Quit();
				driver.Dispose();
				Thread.Yield();
			}
			catch { }
		}
		#endregion
	}
}
