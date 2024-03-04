using System;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using GraphQL.Client.Abstractions;

public class Promotions
{
    public static async Task Main(string[] args)
    {
        using GraphQLHttpClient client = new GraphQLHttpClient("https://api.start.gg/gql/alpha", new NewtonsoftJsonSerializer());

        client.HttpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + Environment.GetEnvironmentVariable("auth"));

        Console.WriteLine("Do you want the program to automatically remove alternates? (y/n) [Defaults to 'yes' on invalid input]");

        string? input = Console.ReadLine();

        bool removeAlternates;

        if (input == null || input.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            removeAlternates = true;
        }
        else if (input.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            removeAlternates = false;
        } else
        {
            removeAlternates = true;
        }

        long eventID = 0;
        Region region = Region.UNKNOWN;

        try
        {
            (eventID, region) = await GetEventID(client);
        }
        catch (GraphQLHttpRequestException e) {
            Console.WriteLine("Request failed. This is most likely due to not providing a valid start.gg API token. The token should be provided as an environment variable with the name 'auth' (without quotes).");
            Console.WriteLine(e.Message);
            Environment.Exit(-1);
        }

        PhaseType phase = await GetPhaseID(client, eventID); //name, id

        List<string>[] promotions = new List<string>[3];
        //0 = BPLUS, 1 = A, 2 = X

        for (int i = 0; i < 3; i++)
        {
            promotions[i] = new List<string>();
        }

        int BPLUS_criteria = 0;
        int A_criteria = 0;
        int X_criteria = 0;

        if (region == Region.NA || region == Region.UNKNOWN)
        {
            if (region == Region.UNKNOWN)
            {
                Console.WriteLine("Specified region is unknown/unsupported. Defaulting to NA region behavior.");
                //or maybe just error here
                region = Region.NA;
            }
            BPLUS_criteria = 48;
            A_criteria = 24;
            X_criteria = 16;
        }
        else if (region == Region.EU)
        {
            BPLUS_criteria = 64;
            A_criteria = 32;
            X_criteria = 16;
        }

        //Top 48 Day 3 B+, Top 24 A => First num is B+ req, 2nd num is A req
        await GetStandings(client, phase.ID, promotions, PromotionRank.BPLUS, BPLUS_criteria, A_criteria, removeAlternates, region);
        //Top 16 X (Main Event) //First num is X req, second num is also X req
        await GetStandings(client, phase.ID, promotions, PromotionRank.A, X_criteria, X_criteria, removeAlternates, region);

        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Promotions.txt");
        using (StreamWriter sw = new StreamWriter(path))
        {
            for (int i = 0; i < 3; i++)
            {
                if ((PromotionRank)i == PromotionRank.BPLUS)
                {

                    sw.WriteLine("**Rank BPLUS Promotions:**");
                    Console.WriteLine("Rank BPLUS Promotions:");
                }
                else if ((PromotionRank)i == PromotionRank.A)
                {
                    sw.WriteLine("**Rank A Promotions:**");
                    Console.WriteLine("Rank A Promotions:");
                }
                else if ((PromotionRank)i == PromotionRank.X)
                {
                    sw.WriteLine("**Rank X Promotions:**");
                    Console.WriteLine("Rank X Promotions:");
                }

                foreach (String gamertag in promotions[i])
                {
                    sw.Write(gamertag + ", ");
                    Console.WriteLine(gamertag);
                }

                sw.Write(Environment.NewLine);
                sw.Write(Environment.NewLine);
                Console.WriteLine("---------------------------");
            }
            sw.Flush();
            sw.Close();
        }

    }

    private static async Task<(long, Region)> GetEventID(GraphQLHttpClient client)
    {
        Console.WriteLine("Please enter the name of the tournament, as shown on the smashgg URL. For example: \"rlcs-2022-23-fall-open-north-america\"");

        string? input = Console.ReadLine();
        //string slug = "rlcs-2021-22-season-fall-split-regional-3-europe";

        if (input == null)
        {
            Console.WriteLine("Invalid ID. Please try again.");
            return await GetEventID(client);
        }

        var tournamentQueryRequest = new GraphQLRequest
        {
            Query = @"
            query TournamentQuery($slug: String) {
                tournament(slug: $slug) {
                    events {
                        id
                        name
                    }
                }
            }",
            OperationName = "TournamentQuery",
            Variables = new
            {
                slug = input
            }
        };

        //var response = await client.SendQueryAsync<>(tournamentQueryRequest);

        var response = await client.SendQueryAsync(tournamentQueryRequest, () => new { tournament = new TournamentType() });
        var tournamentEvents = response.Data.tournament.Events;
        long? id = null;

        foreach (EventType e in tournamentEvents)
        {
            Console.WriteLine(e.ID);
            Console.WriteLine(e.Name);
            Console.WriteLine();

            if (e.Name.Contains("Qualifier", StringComparison.OrdinalIgnoreCase))
            {
                id = e.ID;
            }
        }

        if (id == null)
        {
            throw new Exception("Unable to find valid event to pull from. Ensure the provided string is correct.");
        }

        Region region;

        if (input.Contains("europe", StringComparison.OrdinalIgnoreCase))
        {
            region = Region.EU;
        }
        else if (input.Contains("north-america", StringComparison.OrdinalIgnoreCase))
        {
            region = Region.NA;
        }
        else
        {
            region = Region.UNKNOWN;
        }

        return ((long) id, region);

    }

    private static async Task<PhaseType> GetPhaseID(GraphQLHttpClient client, long eventID)
    {
        var eventQueryRequest = new GraphQLRequest
        {
            Query = @"
            query EventQuery($id: ID) {
                event(id: $id) {
                    id
                    name
                    phases {
                        id
                        name
                    }
                }
            }",
            OperationName = "EventQuery",
            Variables = new
            {
                id = eventID
            }
        };

        Console.WriteLine(eventID);

        var response = await client.SendQueryAsync(eventQueryRequest, () => new { Event = new EventType() });

        //Console.WriteLine(response.Data.ToString());

        var phases = response.Data.Event.Phases;

        PhaseType d3_phase = new PhaseType();

        foreach (PhaseType phase in phases)
        {
            Console.WriteLine(phase.ID);
            Console.WriteLine(phase.Name);
            Console.WriteLine();

            if (phase.Name.Contains("Day 2:", StringComparison.OrdinalIgnoreCase)) {
                //phaseSet.Add(phase);
                //NO LONGER NEEDED
            }
            else if (phase.Name.Contains("Tiebreaker", StringComparison.OrdinalIgnoreCase))
            {
                //skip for now, maybe add processing later if wanted
            }
            else if (phase.Name.Contains("Day 3:", StringComparison.OrdinalIgnoreCase)) {
                d3_phase = phase;
            }
        }

        if (d3_phase == null || d3_phase.Name == null || d3_phase.Name == "") {
            throw new Exception("Found an unexpected number of phases. Has the format changed?");
        }

        /*
        Console.WriteLine(phaseIDs.Count);
        for (int i = 0; i < phaseIDs.Count; i++)
        {
            Console.WriteLine(phaseIDs.ToArray()[i]);
        }
        */

        return d3_phase;
    }

    private static async Task GetStandings(GraphQLHttpClient client, long phaseID, List<string>[] promotions, PromotionRank day, int numPromotingTeams, int cutoff = 0, bool removeAlternates = true, Region region = Region.NA)
    {
        var phaseQueryRequest = new GraphQLRequest();
        if (region == Region.NA)
        {
            phaseQueryRequest = new GraphQLRequest
            {
                Query = @"
            query PhaseQuery($id: ID, $numTeams: Int, $sort: String) {
		        phase(id: $id){
			        name
    	            phaseGroups(query: {
                        page: 1
                        perPage: 1
                    }) {
      	                nodes {
                            standings(query: {
      		                    perPage: $numTeams,
      		                    page: 1,
                                sortBy: $sort
                            }){
      		                    nodes {
        		                    placement
        		                    entrant {
          		                        name
                                        team {
                                            members(status: ACCEPTED) {
                                                isAlternate
                                                player {
                                                    gamerTag
                                                    user {
                                                        authorizations(types: [DISCORD]) {
                                                            externalId
                                                            externalUsername
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
      		                    }
    		                }
                        }
                    }
	            }
            }",
                OperationName = "PhaseQuery",
                Variables = new
                {
                    id = phaseID,
                    numTeams = numPromotingTeams,
                    sort = "placement"
                }
            };
        }
        //if EU:
        if (region == Region.EU)
        {
            phaseQueryRequest = new GraphQLRequest
            {
                Query = @"
            query PhaseQuery($id: ID, $numTeams: Int, $sort: String) {
		        phase(id: $id){
			        name
    	            phaseGroups(query: {
                        page: 1
                        perPage: 1
                    }) {
      	                nodes {
                            standings(query: {
      		                    perPage: $numTeams,
      		                    page: 1,
                                sortBy: $sort
                            }){
      		                    nodes {
        		                    placement
        		                    entrant {
          		                        name
                                        team {
                                            members(status: ACCEPTED) {
                                                isAlternate
                                                player {
                                                    gamerTag
                                                }
                                            }
                                        }
                                    }
      		                    }
    		                }
                        }
                    }
	            }
            }",
                OperationName = "PhaseQuery",
                Variables = new
                {
                    id = phaseID,
                    numTeams = numPromotingTeams,
                    sort = "placement"
                }
            };
        }



        //Gets top "numPromotingTeams" teams.

        var response = await client.SendQueryAsync(phaseQueryRequest, () => new { Phase = new PhaseType() });

        List<StandingsNodeType> standings = response.Data.Phase.PhaseGroups.Nodes[0].Standings.Nodes;

        if (standings.Count != numPromotingTeams)
        {
            throw new Exception("Unknown query failure - query returned an incorrect number of teams.\nExpected: " + numPromotingTeams + " but got: " + standings.Count);
        }

        for (int i = 0; i < standings.Count; i++)
        {
            if (i < cutoff) //Teams placing "Top [Cuttoff] or better"
            {
                foreach (MemberType member in standings.ElementAt(i).Entrant.Team.Members)
                {
                    if (member.isAlternate && removeAlternates)
                    {
                        continue;
                    }
                    string str = member.Player.Gamertag;
                    if (member.Player.User != null && member.Player.User.Authorizations != null)
                    { //Testing Grabbing Discord Info
                        //Console.WriteLine("Rank: " + ((int)day + 1) + " => " + member.Player.User.Authorizations[0].externalId + " (" + member.Player.User.Authorizations[0].externalUsername + ")");
                        str += " (" + member.Player.User.Authorizations[0].externalId + ")";
                    }
                    if (day == PromotionRank.A)
                    {
                        await RemoveDuplicates(str, promotions, ((int)day) + 1);
                    }
                    promotions[((int)day) + 1].Add(str);
                }
            } else //Teams placing Top [numPromotingTeams] but not Top [Cutoff]
            {
                foreach (MemberType member in standings.ElementAt(i).Entrant.Team.Members)
                {
                    if (member.isAlternate && removeAlternates)
                    {
                        continue;
                    }
                    string str = member.Player.Gamertag;
                    if (member.Player.User != null && member.Player.User.Authorizations != null)
                    { //Testing Grabbing Discord Info
                        //Console.WriteLine("Rank: " + ((int)day + 1) + " => " + member.Player.User.Authorizations[0].externalId + " (" + member.Player.User.Authorizations[0].externalUsername + ")");
                        str += " (" + member.Player.User.Authorizations[0].externalId + ")";
                    }
                    if (day == PromotionRank.A)
                    {
                        await RemoveDuplicates(str, promotions, (int)day);
                    }
                    promotions[((int)day)].Add(str);
                }
            }
        }
    }

    private static async Task RemoveDuplicates(String gamertag, List<string>[] promotions, int new_idx)
    {
        for (int i = new_idx; i >= 0; i--)
        {
            if (promotions[i].Contains(gamertag))
            {
                promotions[i].Remove(gamertag);
                return;
            }
        }
    }
}

public class TournamentType
{
    public List<EventType> Events { get; set; }
}

public class EventType
{
    public long ID { get; set; }
    public string Name { get; set; }

    public List<PhaseType> Phases { get; set; }
}

public class PhaseType
{
    public long ID { get; set; }
    public string Name { get; set; }
    public PhaseGroupType PhaseGroups { get; set; }
}

public class PhaseGroupType
{
    public List<PhaseNodeType> Nodes { get; set; }
}

public class PhaseNodeType
{
    public StandingsType Standings { get; set; }
}

public class StandingsType
{
    public List<StandingsNodeType> Nodes { get; set; }
}

public class StandingsNodeType
{
    public int Placement { get; set; }
    public EntrantType Entrant { get; set; }
}

public class EntrantType
{
    public string Name { get; set; }
    public TeamType Team { get; set; }
}

public class TeamType
{
    public List<MemberType> Members { get; set; }
}

public class MemberType
{
    public bool isAlternate { get; set; }
    public PlayerType Player { get; set; }
}

public class PlayerType
{
    public String Gamertag { get; set; }
    public UserType User { get; set; }
}

public class UserType
{
    public List<AuthorizationType> Authorizations { get; set; }
}

public class AuthorizationType
{
    public String externalId { get; set; }
    public String externalUsername { get; set; }
}

public enum PromotionRank
{
    BPLUS = 0, A = 1, X = 2
}

public enum Region
{
    NA = 0, EU = 1, UNKNOWN = 2
}