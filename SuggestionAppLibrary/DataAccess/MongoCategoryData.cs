﻿using Microsoft.Extensions.Caching.Memory;

namespace SuggestionAppLibrary.DataAccess;
public class MongoCategoryData : ICategoryData
{
   private readonly IMongoCollection<CategoryModel> _categories;
   private readonly IMemoryCache _cache;
   private const string cacheName = "CategoryData";


   //using cache in categories because this is data that is not going to change that often so we want to cache it
   public MongoCategoryData(IDBConnection db, IMemoryCache cache)
   {
      _categories = db.CategoryCollection;
      _cache = cache;
   }

   public async Task<List<CategoryModel>> GetAllCategories()
   {
      var output = _cache.Get<List<CategoryModel>>(cacheName);

      if (output is null)
      {
         var results = await _categories.FindAsync(_ => true);
         output = results.ToList();
         _cache.Set(cacheName, output, TimeSpan.FromDays(1));
      }
      return output;
   }

   public Task CreateCategory(CategoryModel category)
   {
      return _categories.InsertOneAsync(category);
   }
}