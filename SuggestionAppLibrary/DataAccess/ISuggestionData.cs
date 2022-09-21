namespace SuggestionAppLibrary.DataAccess;

public interface ISuggestionData
{
   Task CreateSuggestion(SuggestionModel suggestion);
   Task<List<SuggestionModel>> GetAllApprovedSuggestions();
   Task<List<SuggestionModel>> GetAllSuggestions();
   Task<SuggestionModel> GetSuggestion(string id);
   Task<List<SuggestionModel>> GetSuggestionsWaitingForApproval();
   Task<List<SuggestionModel>> GetUserSugggestions(string userId);
   Task UpdateSuggestion(SuggestionModel suggestion);
   Task UpvoteSuggestion(string suggestionId, string userId);
}