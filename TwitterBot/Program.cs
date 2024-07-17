using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chrome.ChromeDriverExtensions;
using OpenQA.Selenium.Support.UI;
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

		private const string TwitterLoginUrl = "https://x.com/i/flow/login";
		private static List<Bot>? Bots;
		private static bool AnonymousMode = false;

		private static string? TwitterTargetUrl;
		private static int SpaceJoinIntervalOffset = 0;
		private static string? CommandUserName;

		private static List<Bot> GetBotsFromDb()
		{
			string preciseSelector = "DISTINCT";
#if DEBUG
			preciseSelector = "TOP 1";
#endif
			List<Bot> bots = [];
			try
			{
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"SELECT {preciseSelector} UserName, EmailId, Password FROM BotDetails WHERE LoginFailure = 0";

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
			var svc = ChromeDriverService.CreateDefaultService();
			var chromeOptions = new ChromeOptions();
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
				"--blink-settings=imagesEnabled=false",
				@$"--user-data-dir=C:\temp\{bot.TwitterUserName}",
			}
			);
			chromeOptions.AddHttpProxy(Configuration.ProxyIp, Configuration.ProxyPort, Configuration.ProxyUserName, Configuration.ProxyPassword);
			var driver = new ChromeDriver(svc, chromeOptions);
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
				Console.WriteLine("Element with locator: '" + elementLocator + "' was not found in current context page.");
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
				Console.WriteLine("Element with locator: '" + elementLocator + "' was not found in current context page.");
				throw;
			}
		}

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
			Action<Bot>? function = null;
			switch (taskType)
			{
				case "/like":
					function = LikeTweetsBot;
					break;
				case "/retweet":
					function = RetweetBot;
					break;
				case "/join":
					function = JoinTwitterSpace;
					break;
				case "/rjoin":
					function = RunTwitterSpacesBot;
					break;
				case "/follow":
					function = FollowBot;
					break;
			}
			var tasks = new List<Task>();
			for (int i = 0; i < Bots.Count; i++)
			{
				var bot = Bots[i];
				if (function is not null)
				{
					tasks.Add(Task.Factory.StartNew(() => function(bot)));
				}
			}
			Task.WaitAll([.. tasks]);
		}
		private static bool LoginToTwitter(ChromeDriver driver, Bot bot)
		{
			var profileIconLocator = By.XPath(@"//a[@data-testid=""AppTabBar_Profile_Link""]");
			try
			{
				driver.Navigate().GoToUrl($"https://x.com/{bot.TwitterUserName}");
				WaitUntilElementClickable(driver, profileIconLocator);
				Console.WriteLine($"{bot.TwitterUserName} has logged in to twitter.");
				return true;
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
						var emailFieldLocator = By.XPath("//*[@id=\"layers\"]/div[2]/div/div/div/div/div/div[2]/div[2]/div/div/div[2]/div[2]/div[1]/div/div[2]/label/div/div[2]/div/input");
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
						var emailFieldLocator = By.XPath("//*[@id=\"layers\"]/div[2]/div/div/div/div/div/div[2]/div[2]/div/div/div[2]/div[2]/div[1]/div/div[2]/label/div/div[2]/div/input");
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

					WaitUntilElementClickable(driver, profileIconLocator);
					Console.WriteLine($"{bot.TwitterUserName} has logged in to twitter.");
					return true;
				}
				catch (Exception)
				{
					Console.WriteLine($"{bot.TwitterUserName} could not log in to twitter.");
					try
					{
						SqlConnection conn = new(ConnectionString);
						conn.Open();
						var sqlQuery = $"Update BotDetails SET LoginFailure = 1 WHERE UserName = '{bot.TwitterUserName}'";

						using SqlCommand command = new(sqlQuery, conn);
						var result = command.ExecuteNonQuery();
						conn.Close();
					}
					catch (Exception) { }
					driver.Dispose();
					Thread.Yield();
					return false;
				}
			}
		}
		private static void LikeTweetsBot(Bot bot)
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
						var likeButtonLocator = By.XPath(@"(//button[@data-testid=""like""])[1]");
						WaitUntilElementClickable(driver, likeButtonLocator);
						var likeButton = driver.FindElement(likeButtonLocator);
						likeButton.Click();
						Console.WriteLine($"{bot.TwitterUserName} has liked the target tweet.");
					}
					catch (Exception) { }
				}
				else
				{
					Console.WriteLine($"{bot.TwitterUserName} could not like target tweet.");
				}
			}
			finally
			{
				driver.Dispose();
				Thread.Yield();
			}
		}
		private static void RetweetBot(Bot bot)
		{
			var driver = GetChromeDriver(bot);
			try
			{
				if (LoginToTwitter(driver, bot))
				{
					Thread.Sleep(new Random().Next(1000, 600000));
					driver.Navigate().GoToUrl(TwitterTargetUrl);
					try
					{
						var retweetButtonLocator = By.XPath(@"(//button[@data-testid=""retweet""])[1]");
						var repostOptionLocator = By.XPath(@"//span[text()='Repost']");
						WaitUntilElementClickable(driver, retweetButtonLocator);
						var retweetButton = driver.FindElement(retweetButtonLocator);
						retweetButton.Click();
						WaitUntilElementClickable(driver, repostOptionLocator);
						var repostButton = driver.FindElement(repostOptionLocator);
						repostButton.Click();
						Console.WriteLine($"{bot.TwitterUserName} has retweeted the target tweet.");
					}
					catch (Exception) { }
				}
				else
				{
					Console.WriteLine($"{bot.TwitterUserName} could not retweet target tweet.");
				}
			}
			finally
			{
				driver.Dispose();
				Thread.Yield();
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
						Console.WriteLine($"{bot.TwitterUserName} has followed the target.");
					}
					catch (Exception) { }
				}
				else
				{
					Console.WriteLine($"{bot.TwitterUserName} could not follow target.");
				}
			}
			finally
			{
				driver.Dispose();
				Thread.Yield();
			}
		}
		private static void RunTwitterSpacesBot(Bot bot)
		{
			var driver = GetChromeDriver(bot);
			try
			{
				if (LoginToTwitter(driver, bot))
				{
					Console.WriteLine($"{bot.TwitterUserName} has logged in to twitter.");
					for (int i = 0; i < 1000; i++)
					{
						if (JoinTwitterSpace(bot, driver))
						{
							Console.WriteLine($"{bot.TwitterUserName} has joined twitter space.");
							Thread.Sleep(new Random().Next(120000, 240000) + (SpaceJoinIntervalOffset * 1000));
						}
					}

				}
			}
			finally
			{
				driver.Dispose();
				Thread.Yield();
			}
		}

		private static void JoinTwitterSpace(Bot bot)
		{
			ChromeDriver driver = GetChromeDriver(bot);
			JoinTwitterSpace(bot, driver);
			Task.Delay(15 * 60 * 1000).ContinueWith((task) =>
			{
				driver.Dispose();
				Thread.Yield();
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
					Console.WriteLine($"{bot.TwitterUserName} could not join twitter space.");
				}
			}
			return false;
		}

		private static void SaveProcessId(Bot bot)
		{
			try
			{
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"INSERT INTO [dbo].[SpaceProcessIds] ([ProcessDate],[CommandUserName],[UserName],[Url],[ProcessId],[ProcessKilled]) VALUES ('{DateTime.Now:yyyy-MM-dd HH:mm:ss}','{CommandUserName}','{bot.TwitterUserName}','{TwitterTargetUrl}',{bot.ProcessId},0)";

				using SqlCommand command = new(sqlQuery, conn);
				var result = command.ExecuteNonQuery();
				conn.Close();
			}
			catch (Exception) { }
		}
		private static void UpdateSpaceUrlToProcessEntry(Bot bot)
		{
			try
			{
				SqlConnection conn = new(ConnectionString);
				conn.Open();
				var sqlQuery = $"UPDATE [dbo].[SpaceProcessIds] SET Url = {TwitterTargetUrl} Where ProcessId = '{bot.ProcessId}' AND UserName = '{bot.TwitterUserName}'";

				using SqlCommand command = new(sqlQuery, conn);
				var result = command.ExecuteNonQuery();
				conn.Close();
			}
			catch (Exception) { }
		}
		private static void ShowEmojisRandomly(ChromeDriver driver)
		{
			var emojisTogglerLocator = By.XPath(@"//*[@id=""layers""]/div/div[1]/div/div/div/div[2]/div/div/div[2]/div/button");
			WaitUntilElementClickable(driver, emojisTogglerLocator);
			var emojisToggler = driver.FindElement(emojisTogglerLocator);
			emojisToggler.Click();

			var emojis = new List<string> {
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[1]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[2]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[3]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[4]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[5]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[6]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[7]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[8]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[9]",
				"//*[@id=\"layers\"]/div[3]/div/div/div[2]/div/div[2]/div/div/div/div/div/div/div/button[10]"
			};

			var rnd = new Random();
			int emojiNumber = rnd.Next(1, 10);
			var laughEmojiLocator = By.XPath(emojis[emojiNumber]);
			WaitUntilElementClickable(driver, laughEmojiLocator);
			var laughEmoji = driver.FindElement(laughEmojiLocator);
			laughEmoji.Click();
		}
	}
}
