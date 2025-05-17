using HearthMirror;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Controls.Error;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Utility.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Threading.Tasks;


namespace HDT_LeaderBoard
{
	public class LeaderBoard
	{
		public bool done = false;
        public bool failToGetData = false;
        public Dictionary<string, string> oppDict = null;

        private bool isReset = true;
        private bool namesReady = false;
        private bool playersReady = false;
        private bool leaderBoardReady = false;

        private readonly HttpClient client;
        private List<string> oppNames = null;
        private Dictionary<string, string> leaderBoard = null;

		public LeaderBoard()
		{
			client = new HttpClient();
			client.DefaultRequestHeaders.Add("User-Agent", "User-Agent-Here");
		}

		private void Reset()
        {
            done = false;
            namesReady = false;
            playersReady = false;
            failToGetData = false;
            leaderBoardReady = false;
            ClearMemory();
        }

        public void ClearMemory()
        {
            oppDict = null;
            oppNames = null;
            leaderBoard = null;
        }

        public void OnGameStart()
        {
            GetLeaderBoard();
        }

        public void OnTurnStart(ActivePlayer player)
        {
            GetLeaderBoard();
            playersReady = true;
        }

        public void OnUpdate() 
        {
            if (Core.Game.IsInMenu)
            {
                if (!isReset)
                {
                    Reset();
                    isReset = true;
                }
            }
            else if (!done && Core.Game.IsBattlegroundsMatch)
            {
                isReset = false;
                if (failToGetData) { done = true; }
                else if (!namesReady) { GetOppNames(); }
                else if (leaderBoardReady)
                {
                    Dictionary<string, int> unsortDict = new Dictionary<string, int>();
                    Dictionary<string, string> positionDict = new Dictionary<string, string>();

                    oppDict = new Dictionary<string, string>();

                    foreach (string name in oppNames)
                    {
                        if (leaderBoard.TryGetValue(name, out string value))
                        {
                            var parts = value.Split('|');
                            int position = int.Parse(parts[0]);
                            int score = int.Parse(parts[1]);

                            unsortDict.Add(name, score);
                            positionDict.Add(name, position.ToString());
                        }
                        else
                        {
                            unsortDict.Add(name, 0);
                            positionDict.Add(name, "500+");
                        }
                    }
                    foreach (var opp in unsortDict.OrderBy(x => x.Value))
                    {
                        string position = positionDict[opp.Key];
                        if (opp.Value == 0)
                        {
                            oppDict.Add(opp.Key, $"500+|{opp.Value}");
                        }
                        else
                        {
                            oppDict.Add(opp.Key, $"{position}|{opp.Value}");
                        }
                    }
                    done = true;
                }
            }
        }

		public class PlayerInfo
		{
			public int position { get; set; }
			public string battle_tag { get; set; }
			public int score { get; set; }
		}

		public class LeaderboardData
		{
			public List<PlayerInfo> list { get; set; }
			public int total { get; set; }
		}

		public class ApiResponse
		{
			public int code { get; set; }
			public string message { get; set; }
			public LeaderboardData data { get; set; }
		}

        private async Task GetLeaderBoard()
        {
            if (!Core.Game.IsBattlegroundsMatch || leaderBoardReady || failToGetData) { return; }

            leaderBoard = new Dictionary<string, string>();
            string path;
            bool isSolo;

            if (Core.Game.IsBattlegroundsSoloMatch)
            {
                isSolo = true;
                path = Path.Combine(Config.AppDataPath, $"LeaderBoard.txt");
            }
            else
            {
                isSolo = false;
                path = Path.Combine(Config.AppDataPath, $"LeaderBoard_duo.txt");
            }

            try
            {
                Log.Info($"Try to get the leaderboard from api using FetchLeaderBoardAsync()");
                var fetchLeaderBoard = await FetchLeaderBoardAsync(isSolo);

                if (fetchLeaderBoard != null && fetchLeaderBoard.Count > 0)
                {
                    leaderBoard = fetchLeaderBoard;
                    leaderBoardReady = true;
                    Log.Info("Successfully fetched leaderboard data.");

                    using (StreamWriter writer = new StreamWriter(path))
                    {
                        foreach (var player in leaderBoard)
                        {
                            var parts = player.Value.Split('|');
                            writer.WriteLine($"{player.Key} {parts[0]} {parts[1]}");
                        }
                            
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("FetchLeaderBoardAsync failed: " + ex.Message);
            }

            //如果网络失败，则从本地加载
            Log.Info("Try to get leaderboard from local file...");
            if (File.Exists(path))
            {
                try
                {
                    using (StreamReader reader = new StreamReader(path))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string[] tmp = line.Split(' ');
                            if (tmp.Length == 2)
                                leaderBoard.Add(tmp[0], tmp[1]);
                        }
                    }
                    if (leaderBoard.Count > 0)
                    {
                        leaderBoardReady = true;
                        Log.Info("Successfully loaded leaderboard from local");
                    }
                    else
                    {
                        failToGetData = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to read local leaderboard: " + ex.Message);
                    failToGetData = true;
                }
            }
            else
            {
                failToGetData = true;
            }

            if (failToGetData)
            {
                Log.Info("Fail to get leaderboard data from web and local.");
            }
        }

		private async Task<Dictionary<string, string>> FetchLeaderBoardAsync(bool isSolo)
		{
			Dictionary<string, string> leaderboard = new Dictionary<string, string>();
			HttpClient client = new HttpClient();
            string mode = isSolo ? "battlegrounds" : "battlegroundsduo";
            
            int num_tries = 0;
            int max_tries = 3;

            for (int page = 1; page <= 20; page++)
			{
                if (num_tries >= max_tries)
                {
                    Console.WriteLine($"[LeaderBoard] Max retry limit reached. Stopping at page {page}.");
                    break;
                }

				string url = $"https://webapi.blizzard.cn/hs-rank-api-server/api/game/ranks?page={page}&page_size=25&mode_name={mode}&season_id=15";
				
                try
				{
					string json = await client.GetStringAsync(url);
					var response = JsonConvert.DeserializeObject<ApiResponse>(json);

					if (response.code == 0 && response.data?.list != null)
					{
						foreach (var player in response.data.list)
						{
							if (!leaderboard.ContainsKey(player.battle_tag))
								leaderboard[player.battle_tag] = $"{player.position}|{player.score}";
						}

                        //重置失败次数
                        num_tries = 0;
					}
                    else
                    {
                        num_tries++;
                        Console.WriteLine($"[LeaderBoard] Error response on page {page}. Retry count: {num_tries}");
                    }
				}
				catch (Exception ex)
				{
                    num_tries++;
					Console.WriteLine($"[LeaderBoard] Failed to fetch page {page}: {ex.Message}. Retry count: {num_tries}");    
                }

				// 延迟避免触发请求频率限制（可选）
				await Task.Delay(100);
			}

			return leaderboard;
		}

        private void GetOppNames()
        {
            if (!playersReady) { return; }

            // The code below is from: https://github.com/Zero-to-Heroes/unity-spy-.net4.5
            try
            {
                string myName = Reflection.Client?.GetBattleTag()?.Name;
                if (string.IsNullOrEmpty(myName)) { return; }
                Mirror mirror = new Mirror { ImageName = "Hearthstone" };
                var leaderboardMgr = mirror.Root?["PlayerLeaderboardManager"]?["s_instance"];
                if (leaderboardMgr == null) { return; }
                dynamic[] playerTiles = GetPlayerTiles(leaderboardMgr);
                var numberOfPlayerTiles = playerTiles?.Length ?? 0;
                if (numberOfPlayerTiles == 0) { return; }
                List<string> tmpNames = new List<string>();

                for (int i = 0; i < numberOfPlayerTiles; i++)
                {
                    var playerTile = playerTiles[i];
                    // Info not available until the player mouses over the tile in the leaderboard, and there is no other way to get it
                    string playerName = playerTile["m_overlay"]?["m_heroActor"]?["m_playerNameText"]?["m_Text"];
                    if (string.IsNullOrEmpty(playerName)) { return; }
                    if (playerName != myName && !tmpNames.Contains(playerName)) { tmpNames.Add(playerName); }
                }

                // For those who use the BattleTag MOD
                for (int i = 0; i < tmpNames.Count; i++)
                {
                    int index = tmpNames[i].IndexOf('#');
                    if (index > 0)
                    {
                        tmpNames[i] = tmpNames[i].Substring(0, index);
                    }
                }
                oppNames = new List<string>(tmpNames);
                namesReady = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        // The code below is from: https://github.com/Zero-to-Heroes/unity-spy-.net4.5
        private static dynamic[] GetPlayerTiles(dynamic leaderboardMgr)
        {
            try
            {
                return leaderboardMgr?["m_playerTiles"]?["_items"];
            }
            catch (Exception)
            {
                var result = new List<dynamic>();
                var teams = leaderboardMgr["m_teams"]?["_items"];

                for (uint i = 0; i < teams.size(); i++)
                {
                    var team = teams[i];
                    if (team == null) { continue; }
                    var tiles = team["m_playerLeaderboardCards"]?["_items"];
                    for (uint j = 0; j < tiles.size(); j++)
                    {
                        result.Add(tiles[j]);
                    }
                }

                return result.ToArray();
            }
        }



    }
}