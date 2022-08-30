
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics.Metrics;

namespace SuggestionAppLibrary.DataAccess;
public class MongoSuggestionData : ISuggestionData
{
   private readonly IDBConnection _db;
   private readonly IUserData _userData;
   private readonly IMemoryCache _cache;
   private readonly IMongoCollection<SuggestionModel> _suggestions;
   private const string CacheName = "SuggestionData";


   public MongoSuggestionData(IDBConnection db, IUserData userData, IMemoryCache cache)
   {
      _db = db;
      _userData = userData;
      _cache = cache;
      _suggestions = db.SuggestionCollection;
   }

   public async Task<List<SuggestionModel>> GetAllSuggestions()
   {
      var output = _cache.Get<List<SuggestionModel>>(CacheName);

      if (output is null)
      {
         var results = await _suggestions.FindAsync(s => s.Archived == false);
         output = results.ToList();

         _cache.Set(CacheName, output, TimeSpan.FromMinutes(1));
      }
      return output.ToList();
   }

   public async Task<List<SuggestionModel>> GetAllApprovedSuggestions()
   {
      var output = await GetAllSuggestions();
      return output.Where(x => x.ApprovedForRelease).ToList();
   }

   public async Task<SuggestionModel> GetSuggestion(string id)
   {
      //we are not using cache because: it is just one item being loaded to memory as opposed to
      //hundreds in the getAllSuggestion being loaded all at once into memory
      var results = await _suggestions.FindAsync(s => s.Id == id);
      return results.FirstOrDefault();
   }

   public async Task<List<SuggestionModel>> GetSuggestionsWaitingForApproval()
   {
      var output = await GetAllSuggestions();
      return output.Where(x =>
         x.ApprovedForRelease == false
         && x.Rejected == false).ToList();
   }

   public async Task UpdateSuggestion(SuggestionModel suggestion)
   {
      await _suggestions.ReplaceOneAsync(s => s.Id == suggestion.Id, suggestion);
      _cache.Remove(CacheName);
   }

   public async Task UpvoteSuggestion(string suggestionId, string userId)
   {
      var client = _db.Client;

      //creates a transaction which will allow us to make sure when we write to 2 different collection it
      //either completely succeds or completely fails
      using var session = await client.StartSessionAsync();

      session.StartTransaction();

      try
      {
         var db = client.GetDatabase(_db.DbName);
         var suggestionInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
         //using First rather than FirstOrDefault because First will throw an exception if no suggestion found 
         //we want that rather than continue code execution
         var suggestion = (await suggestionInTransaction.FindAsync(s => s.Id == suggestionId)).First();

         //Hashset does not allow duplicate values. So if user has already updated this sugg, will return false
         bool isUpvoted = suggestion.UserVotes.Add(userId);

         if (isUpvoted == false)
         {
            suggestion.UserVotes.Remove(userId);
         }

         await suggestionInTransaction.ReplaceOneAsync(s => s.Id == suggestionId, suggestion);

         var userInTransactions = db.GetCollection<UserModel>(_db.UserCollectionName);
         var user = await _userData.GetUser(suggestion.Author.Id);

         if (isUpvoted)
         {
            user.VotedOnSuggestions.Add(new BasicSuggestionModel(suggestion));
         }
         else
         {
            var suggestionToRemove = user.VotedOnSuggestions.Where(s => s.Id == suggestionId).First();
            user.VotedOnSuggestions.Remove(suggestionToRemove);
         }
         await userInTransactions.ReplaceOneAsync(u => u.Id == userId, user);

         await session.CommitTransactionAsync();

         //refresh the cache
         _cache.Remove(CacheName);
      }
      catch (Exception ex)
      {
         await session.AbortTransactionAsync();
         //exception will go all the way up to the caller.
         throw;
      }
   }

   public async Task CreateSuggestion(SuggestionModel suggestion)
   {
      var client = _db.Client;

      using var session = await client.StartSessionAsync();

      session.StartTransaction();

      try
      {
         var db = client.GetDatabase(_db.DbName);
         var suggestionInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
         await suggestionInTransaction.InsertOneAsync(suggestion);

         //retrieve user
         var userInTransactions = db.GetCollection<UserModel>(_db.UserCollectionName);
         var user = await _userData.GetUser(suggestion.Author.Id);
         //update authoredSuggestion
         user.AuthoredSuggestions.Add(new BasicSuggestionModel(suggestion));
         //Replace user with new modified user
         await userInTransactions.ReplaceOneAsync(u => u.Id == user.Id, user);

         await session.CommitTransactionAsync();

      }
      catch (Exception ex)
      {
         await session.AbortTransactionAsync();
         throw;
      }

   }
}
