using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Newtonsoft.Json.Linq;
using OfficeOpenXml;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Math;

namespace Lowes.Catalog.Importer
{
	internal class Program
	{

		static List<Dictionary<string, object>> fileCatalog = new List<Dictionary<string, object>>();
		static Dictionary<string, string> itemNumberProductIdDictionary = new Dictionary<string, string>(); 
		static List<string> dontDupeItemsList = new List<string>();
		// track which categories, room types, and collections we have as find them
		static List<string> categoriesList = new List<string>();
		static List<string> roomTypesList = new List<string>();
		static List<string> collectionsList = new List<string>();
		static List<string> spatialCategoriesList = new List<string>();
		static List<Dictionary<string, object>> bundleRowObjectsList = new List<Dictionary<string, object>>();
		const string SpaceName = "spatialcat";

		private static void Main(string[] args)
		{
			try
			{

				if (args.Length > 0)
				{
					var filename = args[0];
					ClearOldData();
					CreateSchema();
					CreateHardcodedElements();
					if (LoadFileIntoMemory(filename))
					{
						CreateProductDictionaryFromItemNumbers();
						foreach (var row in fileCatalog)
						{
							CreateRoomType(row);
							CreateCategory(row);
							CreateSpatialCategory(row);
							if (CreateCollection(row)) continue;

							WriteProductToDb(row);

							if (ShouldSkipByPriorityAndBundle(row))
							{
								continue;
							}
							   
							WriteGroupToDb(row);
						}
					}

					// now do all the bundles
					foreach (var row in bundleRowObjectsList)
					{
						WriteGroupToDb(row);
					}
				}
				else
				{
					Console.WriteLine("The first argument should be an Excel file. Proper use is Lowes.Catalog.Importer.exe [filename].");
				}
			}
			catch (Exception ex)
			{
				Console.BackgroundColor = ConsoleColor.Red;
				Console.ForegroundColor = ConsoleColor.Gray;
				Console.WriteLine(ex.Message);
				Console.ResetColor();
			}

			Console.Write("All done. <ENTER> to continue.");
			Console.ReadLine();
		}

		private static void CreateHardcodedElements()
		{
		    try
		    {
		        using (var context = new lowes_catalogEntities())
		        {

		            var rt = new roomtype {name = "Bath"};
		            context.roomtypes.Add(rt);
                    roomTypesList.Add("Bath");

		            rt = new roomtype {name = "Kitchen"};
		            context.roomtypes.Add(rt);
                    roomTypesList.Add("Kitchen");

		            rt = new roomtype {name = "Other"}; // blank imples both
		            context.roomtypes.Add(rt);
                    roomTypesList.Add("Other");

		            var cat = new category {name = "Floor Tile"};
		            context.categories.Add(cat);
                    categoriesList.Add("Floor Tile");

		            cat = new category {name = "Lighting"};
		            context.categories.Add(cat);
                    categoriesList.Add("Lighting");

		            cat = new category {name = "Mirrors"};
		            context.categories.Add(cat);
                    categoriesList.Add("Mirrors");

		            cat = new category {name = "Paint"};
		            context.categories.Add(cat);
                    categoriesList.Add("Paint");

		            cat = new category {name = "Vanity"};
		            context.categories.Add(cat);
                    categoriesList.Add("Vanity");

		            cat = new category {name = "Wall Tile"};
		            context.categories.Add(cat);
                    categoriesList.Add("Wall Tile");

		            cat = new category {name = "Faucet"};
		            context.categories.Add(cat);
                    categoriesList.Add("Faucet");

		            cat = new category {name = "Cabinet"};
		            context.categories.Add(cat);
                    categoriesList.Add("Cabinet");

		            context.SaveChanges();
                    
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Bath', 'Faucet')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Bath', 'Floor Tile')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Bath', 'Lighting')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Bath', 'Mirrors')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Bath', 'Paint')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Bath', 'Vanity')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Kitchen', 'Cabinet')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Kitchen', 'Floor Tile')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Kitchen', 'Lighting')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Kitchen', 'Paint')");
					context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Kitchen', 'Wall Tile')");
                    

		        }
		    }
		    catch (Exception ex)
		    {
                Console.WriteLine("Error creating hardcoded field. ex: {0}", ex.Message);
		    }

		}

		private static bool LoadFileIntoMemory(string filename)
		{
			if (String.IsNullOrEmpty(filename) || !filename.Contains("xlsx"))
			{
				Console.WriteLine("It doesn't look like a valid *.xlsx was used as an input parameter.");
				return false;
			}

			// Get the file we are going to process
			var existingFile = new FileInfo(filename);
			// Open and read the XlSX file.
			using (var package = new ExcelPackage(existingFile))
			{
				// Get the work book in the file
				var workBook = package.Workbook;
				if (workBook != null)
				{
					if (workBook.Worksheets.Count > 0)
					{
						// Get the first worksheet - "Bath for Canvas"
						var currentWorksheet = workBook.Worksheets.First();
						var dim = currentWorksheet.Dimension;

						// get the column order first
						List<string> headers = new List<string>();
						try
						{
							for (int c = 1; c <= dim.Columns; c++)
							{
								headers.Add(currentWorksheet.Cells[1, c].Value.ToString().ToLower().Trim());
							}
							Console.WriteLine("Column headers loaded.");
						}
						catch (Exception ex)
						{
							Console.BackgroundColor = ConsoleColor.Red;
							Console.ForegroundColor = ConsoleColor.Gray;
							Console.WriteLine("Error creating header collection: {0}", ex.Message);
							Console.ResetColor();
						}

						// go row by row, after the header
						for (int rowindex = 2; rowindex <= dim.Rows; rowindex++)
						{
							Console.WriteLine("reading row {0}", rowindex);
							// first read the row into an array
							Dictionary<string, object> rowObjects = new Dictionary<string, object>();
							try
							{
								for (int colindex = 0; colindex < dim.Columns; colindex++)
								{
									rowObjects[headers[colindex]] = currentWorksheet.Cells[rowindex, colindex + 1].Value;
								}
							}
							catch (Exception ex)
							{
								Console.BackgroundColor = ConsoleColor.Red;
								Console.ForegroundColor = ConsoleColor.Gray;
								Console.WriteLine("Error reading row index {0}: {1}", rowindex, ex.Message);
								Console.ResetColor();
							}

							int nullcount = rowObjects.Count(rowObject => rowObject.Value == null);
							if (rowObjects.Count() > nullcount + 5)
							{
								fileCatalog.Add(rowObjects);
							}
							else
							{
								Console.WriteLine("skipping a row because it has {0} null fields.", nullcount);
							}
						}
					}
				}
			}
			return true;
		}
		
		private static bool ShouldSkipByPriorityAndBundle(Dictionary<string, object> rowObjects)
		{
			bool shouldskip = false;
		    try
		    {
		        shouldskip = (rowObjects.ContainsKey("priority level") && rowObjects["priority level"] != null) &&
		                     (rowObjects["priority level"].ToString() != "1");

		        // if this row is a bundle, save it for later and continue
		        if (rowObjects.ContainsKey("bundle") && rowObjects["bundle"] != null &&
		            rowObjects["bundle"].ToString().Contains(','))
		        {
		            Console.WriteLine("saving bundle for later {0}", rowObjects["url description"].ToString());
		            bundleRowObjectsList.Add(rowObjects);
		            shouldskip = true;
		        }
		    }
		    catch (Exception ex)
		    {
		        Console.WriteLine("the weirdest thing happened...");
		    }
		    return shouldskip;
		}

		private static void CreateSpatialCategory(Dictionary<string, object> rowObjects)
		{
			var space = new spatialcategory {name = SpaceName};
			const string key = "spatial category";
			if (rowObjects.ContainsKey(key) && !String.IsNullOrEmpty(rowObjects[key].ToString().Trim()))
			{
				space.name = rowObjects[key].ToString().Trim();
			}

			if (!spatialCategoriesList.Contains(space.name))
			{
				spatialCategoriesList.Add(space.name);
				using (var context = new lowes_catalogEntities())
				{
					context.spatialcategories.Add(space);
					context.SaveChanges();
				}
			}

			// this needs the be made an association when we're parsing the group.
		}

		/// <summary>
		/// create a collection from this row as needed
		/// </summary>
		/// <param name="currentRow"></param>
		/// <returns>returns true idf this row was added to the bundle list to be parsed later, false if no need to change control flow</returns>
		private static bool CreateCollection(Dictionary<string, object> currentRow)
		{
			if (!currentRow.ContainsKey("collection name"))
			{
				return false;
			}
			// if this is a new collection, add it to the list and create that entity
			var collec = new collection();
			try
			{
				if (!collectionsList.Contains(currentRow["collection name"]) && currentRow["collection name"] != null &&
					!String.IsNullOrEmpty(currentRow["collection name"].ToString()))
				{
					// without a room type, or with multiple room types, we can't make a collection. this happens when the first item parsed in a collection is a tile or paint, etc.
					if (currentRow["room type"] == null || currentRow["room type"].ToString().IndexOfAny(new[] {',', '&'}) != -1)
					{
						bundleRowObjectsList.Add(currentRow);
						return true;
					}

                    var collections = currentRow["collection name"].ToString().Trim().Split(new string[] { "   ", "," }, StringSplitOptions.RemoveEmptyEntries);
					foreach (var collection in collections)
					{
						if (!collectionsList.Contains(collection.Trim()))
						{
							collec.name = collection.Trim();
							collec.roomType = currentRow["room type"].ToString().Trim();
							collec.imageUrl =
								@"http://about-interior-design.net/gallery/contemporary-bathroom-design-ideas/contemporary_bathroom_design_ideas_4.jpg";
							using (var context = new lowes_catalogEntities())
							{
								using (var tran = new TransactionScope())
								{
									context.collections.Add(collec);
									context.SaveChanges();
									Console.ForegroundColor = ConsoleColor.Yellow;
									Console.WriteLine("added collection: {0}", collec.name);
									Console.ResetColor();
									tran.Complete();
								}
							}
							collectionsList.Add(collec.name);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.BackgroundColor = ConsoleColor.DarkGray;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("Error creating collection {0}.", collec.name);
				Console.WriteLine(ex.Message);
				Console.ResetColor();
			}
			return false;
		}

		private static void CreateCategory(Dictionary<string, object> currentRow)
		{
			var cl = new category();
			try
			{
				// if this is a new category, add it to the list and create that entity
				if (currentRow["product category"] != null && !categoriesList.Contains(currentRow["product category"]))
				{
					cl.name = currentRow["product category"].ToString().Trim();
					categoriesList.Add(cl.name);
					using (var context = new lowes_catalogEntities())
					{
						using (var tran = new TransactionScope())
						{
							context.categories.Add(cl);
							context.SaveChanges();
							Console.ForegroundColor = ConsoleColor.Yellow;
							Console.WriteLine("added category: {0}", cl.name);
							Console.ResetColor();
							tran.Complete();
						}

					    if (currentRow["room type"] != null && currentRow["room type"].ToString().IndexOfAny(new[] {',', '&'}) == -1)
					    {
					        string name = currentRow["room type"].ToString().Trim();
                            Console.WriteLine("going to insert roomtypes_categories {0} {1}", name, cl.name);
                            var cmd = String.Format("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('{0}', '{1}')", 
                                name, 
                                cl.name);
					        context.Database.ExecuteSqlCommand(cmd);

                            // context.Database.ExecuteSqlCommand("INSERT INTO roomtypes_categories (roomTypename, categoryName) VALUES ('Bath', 'Faucet')");
					    }
					}
				}
			}
			catch (Exception ex)
			{
				Console.BackgroundColor = ConsoleColor.DarkGray;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("Error creating product category {0}.", cl.name);
				Console.WriteLine(ex.Message);
				Console.ResetColor();
			}
		}

		private static void CreateRoomType(Dictionary<string, object> currentRow)
		{
// if this is a new room type, add it to the list and create that entity
			var rt = new roomtype();
			try
			{
				if (currentRow["room type"] != null && currentRow["room type"].ToString().IndexOfAny(new[] {',', '&'}) == -1)
				{
					rt.name = currentRow["room type"].ToString().Trim();
					if (!roomTypesList.Contains(currentRow["room type"]))
					{
						roomTypesList.Add(rt.name);
						using (var context = new lowes_catalogEntities())
						{
							context.roomtypes.Add(rt);
							context.SaveChanges();
							Console.ForegroundColor = ConsoleColor.Yellow;
							Console.WriteLine("added room type: {0}", rt.name);
							Console.ResetColor();
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.BackgroundColor = ConsoleColor.DarkGray;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("Error creating room type {0}.", rt.name);
				Console.WriteLine(ex.Message);
				Console.ResetColor();
			}
		}


		private static void CreateProductDictionaryFromItemNumbers()
		{
			// build a list of item numbers
			var itemNumList = new List<string>();
			foreach (var row in fileCatalog)
			{
				// get the item ids from the item# and bundle columns, make a list of those and use it to get product ids
				if (row.ContainsKey("item #") && row["item #"] != null)
				{
					var i = row["item #"].ToString().Trim();
					if (!itemNumList.Contains(i))
					{
						itemNumList.Add(i);
					}
				}


				if (row.ContainsKey("bundle") && row["bundle"] != null)
				{
                    var items = row["bundle"].ToString().Split(new string[] { "   ", "," }, StringSplitOptions.RemoveEmptyEntries);
					foreach (var item in items)
					{
						if (!itemNumList.Contains(item.Trim()))
						{
							itemNumList.Add(item.Trim());
						}
					}
				}
			}

			// now make the product list
			MakeProductIdDictionary(itemNumList);
		}

		private static void MakeProductIdDictionary(List<string> itemNumbers)
		{
			try
			{
				HttpClient client = new HttpClient {BaseAddress = new Uri("http://api.lowes.com/product/itemnumber")};
				// Add an Accept header for JSON format.
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
					"QWRvYmU6ZW9pdWV3ZjA5ZmV3bw==");

				String paramBase = "?maxResults=15&api_key=m5d4zhfcahfhkxcwqzg2uqtp";
				StringBuilder sb = new StringBuilder(paramBase, 16*16);
				int recordCount = 0;

				foreach (var itemNumber in itemNumbers)
				{
					sb.Append("&itemNumber=");
					sb.Append(itemNumber);

					// send every X items or if this is the last item
					bool shouldSend = ++recordCount%5 == 0 || recordCount >= itemNumbers.Count;

					if (shouldSend)
					{
						// send it off
						HttpResponseMessage response = client.GetAsync(sb.ToString()).Result; // Blocking call!
						if (response.IsSuccessStatusCode)
						{
							// Parse the response body. Blocking!
							var dataObjects = response.Content.ReadAsStringAsync().Result;
							JObject o = JObject.Parse(dataObjects);
							var numRecords = o["productList"].Count();

							for (int i = 0; i < numRecords; i++)
							{
								string itemNum = (string) o["productList"][i]["itemNumber"];
								string productId = (string) o["productList"][i]["productId"];
								// get the product ID, and inster it in the itemNumbers dictionary with the itemNumber as a key
								if (!itemNumberProductIdDictionary.ContainsKey(itemNum))
								{
									Console.WriteLine("add new product ID mapping. item: {0}, product{1}", itemNum, productId);
									itemNumberProductIdDictionary.Add(itemNum, productId);
								}
							}
						}
						else
						{
							Console.ForegroundColor = ConsoleColor.Black;
							Console.BackgroundColor = ConsoleColor.DarkRed;
							Console.WriteLine("{0} ({1})", (int) response.StatusCode, response.ReasonPhrase);
							Console.ResetColor();
						   
						}

						sb.Length = 0;
						sb.Append(paramBase);

						// wait to make another call.
						Thread.Sleep(600);
					}
				}
			}
			catch (Exception ex)
			{
				Console.BackgroundColor = ConsoleColor.Red;
				Console.ForegroundColor = ConsoleColor.Black;
				Console.WriteLine("Problem creating poduct ID list. Exception: {1}", ex.Message);
				Console.ResetColor();
			}
		}

		private static void WriteProductToDb(Dictionary<string, object> currentRow)
		{
			try
			{
				var product = new product();
				if (currentRow["item #"] != null) product.itemNumber = currentRow["item #"].ToString().Trim();
			    if (currentRow.ContainsKey("national price"))
			    {
			        product.networkPrice = Convert.ToDecimal(currentRow["national price"]);
                }
                else if (currentRow.ContainsKey("local price"))
                {
                    product.networkPrice = Convert.ToDecimal(currentRow["local price"]);
                }

				if (currentRow["model #"] != null) product.modelId = currentRow["model #"].ToString().Trim();

				try
				{
				    if (itemNumberProductIdDictionary.ContainsKey(product.itemNumber))
				    {
				        product.productId = itemNumberProductIdDictionary[product.itemNumber];
				    }
				    else
                    {
                        Console.BackgroundColor = ConsoleColor.White;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine("no PRODUCT ID for ITEM#: {0}. ", product.itemNumber);
                        Console.ResetColor();
				    }
				}
				catch (Exception ex)
				{
					Console.BackgroundColor = ConsoleColor.DarkYellow;
					Console.ForegroundColor = ConsoleColor.Black;
					Console.WriteLine("ITEM#: {0}. Exception: {1}", product.itemNumber, ex.Message);
					Console.ResetColor();
				}

				using (var context = new lowes_catalogEntities())
				{
					if (!context.products.Any(x => x.itemNumber == product.itemNumber))
					{
						using (var tran = new TransactionScope())
						{
							context.products.Add(product);
							context.SaveChanges();
							Console.ForegroundColor = ConsoleColor.Magenta;
							Console.WriteLine("added item: {0}", product.itemNumber);
							Console.ResetColor();
							tran.Complete();
						}
					}
				}
			}
			catch (DbUpdateException ex)
			{
				Console.BackgroundColor = ConsoleColor.DarkYellow;
				Console.ForegroundColor = ConsoleColor.Black;
				if (ex.InnerException != null)
				{
					if (ex.InnerException.InnerException != null)
					{
						Console.WriteLine("INNER INNER: {0}:", ex.InnerException.InnerException.Message);
					}
					else
					{
					Console.WriteLine("INNER: {0}:", ex.InnerException.Message);
					}
				}
				else
				{
					Console.WriteLine(ex.Message);
				}
				Console.ResetColor();
			}
			catch (DbEntityValidationException ex)
			{
				Console.BackgroundColor = ConsoleColor.Red;
				Console.ForegroundColor = ConsoleColor.Gray;
				Console.WriteLine(ex.Message);
				Console.ResetColor();
			}
			catch (Exception ex)
			{
				Console.BackgroundColor = ConsoleColor.Red;
				Console.ForegroundColor = ConsoleColor.Gray;
				Console.WriteLine(ex.Message);
				Console.ResetColor();
			}
		}

		private static void WriteGroupToDb(Dictionary<string, object> rowObjects)
		{
			var group = new @group();
			string itemnum;
			try
			{
				if (rowObjects["item #"] == null)
				{
					// bail out, Goose! Bail out!
					Console.BackgroundColor = ConsoleColor.Red;
					Console.ForegroundColor = ConsoleColor.Gray;
					Console.WriteLine("Can't write items to the DB with no Item ID.");
					Console.ResetColor();
					return;
				}

				itemnum = rowObjects["item #"].ToString().Trim();

				if (dontDupeItemsList.Contains(itemnum))
				{
					Console.BackgroundColor = ConsoleColor.DarkBlue;
					Console.ForegroundColor = ConsoleColor.White;
					Console.WriteLine("There was a duplicate item # in this spreadsheet. It was: {0}", itemnum);
					Console.ResetColor();
					//return;
				}
				dontDupeItemsList.Add(itemnum);

				using (var context = new lowes_catalogEntities())
				{

					@group.imageUrl = "http://lorempixel.com/400/200";

					const string key = "spatial category";
					var space = new spatialcategory { name = SpaceName };
					if (rowObjects.ContainsKey(key) && !String.IsNullOrEmpty(rowObjects[key].ToString().Trim()))
					{
						space.name = rowObjects[key].ToString().Trim();
					}

					if (rowObjects["product category"] != null)
						@group.category = rowObjects["product category"].ToString();
					if (rowObjects["url description"] != null)
						@group.description = rowObjects["url description"].ToString();

					if (rowObjects["room type"] != null)
					{
						if (rowObjects["room type"].ToString().IndexOfAny(new[] {',', '&'}) == -1)
						{
							@group.roomType = rowObjects["room type"].ToString();
						}
					}

					if (rowObjects.ContainsKey("spatial category")) @group.spatialCategory = rowObjects["spatial category"].ToString().Trim();
				    if (String.IsNullOrEmpty(group.spatialCategory))
				    {
				        group.spatialCategory = SpaceName;
                        Console.WriteLine("No spatial category on item:{0}", itemnum);
				    }

					if (rowObjects["national price"] != null) @group.unitPrice = Convert.ToDecimal(rowObjects["national price"]);
					if (rowObjects["notes"] != null) @group.marketingBullets = rowObjects["notes"].ToString();
					if (rowObjects["size"] != null) @group.size = rowObjects["size"].ToString();

					@group.isEnabled = true;
					if (rowObjects.ContainsKey("in canvas") && rowObjects["in canvas"] != null)
					{
						@group.isEnabled = rowObjects["in canvas"].ToString() == "Yes";
					}

					if ((rowObjects["filter 1"] != null) && rowObjects["filter 1"].ToString().ToLower() == "color")
					{
						if (rowObjects["drop down for filter 1"] != null)
							@group.color = rowObjects["drop down for filter 1"].ToString().Replace("   ", ",").Replace("    ", ",").Replace("     ", ",");
					}

					if ((rowObjects["filter 2"] != null) && rowObjects["filter 2"].ToString().ToLower() == "finish")
					{
						if (rowObjects["drop down for filter 2"] != null)
                            @group.Finish = rowObjects["drop down for filter 2"].ToString().Replace("   ", ",").Replace("    ", ",").Replace("     ", ",");
					}

					if ((rowObjects["filter 2"] != null) &&
						rowObjects["filter 2"].ToString().ToLower() == "material")
					{
						if (rowObjects["drop down for filter 2"] != null)
                            @group.material = rowObjects["drop down for filter 2"].ToString().Replace("   ", ",").Replace("    ", ",").Replace("     ", ","); ;
					}

					if ((rowObjects["filter 2"] != null) && rowObjects["filter 2"].ToString().ToLower() == "color")
					{
						if (rowObjects["drop down for filter 2"] != null)
                            @group.color = rowObjects["drop down for filter 2"].ToString().Replace("   ", ",").Replace("    ", ",").Replace("     ", ","); ;
					}

					try
					{
						using (var tran = new TransactionScope())
						{
							context.groups.Add(@group);
							context.SaveChanges();
							Console.ForegroundColor = ConsoleColor.Cyan;
							Console.WriteLine("added groupid: {0}", @group.id);
							Console.ResetColor();
							tran.Complete();
						}
					}
					catch (Exception ex)
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine(ex.Message);
						Console.ResetColor();

					}

					try
					{
						// if this is a bundle, get the products in the bundle
						if (rowObjects["bundle"] != null && rowObjects["bundle"].ToString().Contains(','))
						{
                            var itemnumbers = rowObjects["bundle"].ToString().Split(new string[] { "   ", "," }, StringSplitOptions.RemoveEmptyEntries);
							//group.products = context.products.Where(p => itemnumbers.Contains(p.itemNumber)).ToList();
							foreach (var i in itemnumbers)
							{
								//    //p.groups.Add(group);
								//    group.products.Add(p); //  <-- this makes a duplicate key
                                var productid = itemNumberProductIdDictionary[i.Trim()];
								string script = String.Format("INSERT INTO groups_products (groupId, productId) VALUES ({0},{1})", @group.id, productid);
								context.Database.ExecuteSqlCommand(script);
							}
						}
						else
						{
							//group.products = context.products.Where(x => x.itemNumber == itemnum).ToList();
							string script =
								String.Format("INSERT INTO groups_products (groupId, productId) VALUES ({0},'{1}')",
									@group.id,
									itemNumberProductIdDictionary[rowObjects["item #"].ToString().Trim()]);
							context.Database.ExecuteSqlCommand(script);
						}
					} catch (Exception ex)
					{
						Console.BackgroundColor = ConsoleColor.Red;
						Console.ForegroundColor = ConsoleColor.Gray;
						Console.WriteLine("Problem saving groups_products for group {0}. message: {1}", group.id, ex.Message);
						Console.ResetColor();
					}

					try
					{
						// if this group is in a collection, let's make that happen now.
						if (rowObjects.ContainsKey("collection name") && rowObjects["collection name"] != null)
						{
                            var cnames = rowObjects["collection name"].ToString().Trim().Split(new string[] { "   ", "," }, StringSplitOptions.RemoveEmptyEntries);
						    foreach (var cname in cnames)
						    {
						        var cg = new collections_groups
						        {
						            @group = @group,
						            collection = context.collections.FirstOrDefault(c => (c.name == cname.Trim()))
						        };
						        context.collections_groups.Add(cg);
                                
                                Console.ForegroundColor = ConsoleColor.Blue;
                                Console.WriteLine("Added collection_group group:{0} collection: {1}", group.id, cname.Trim());
                                Console.ResetColor();
						    }
						}
					}
					catch (Exception ex)
					{
						Console.BackgroundColor = ConsoleColor.Red;
						Console.ForegroundColor = ConsoleColor.Gray;
						Console.WriteLine("Problem saving collections_groups. message: {0}", ex.Message);
						Console.ResetColor();
					}

					context.SaveChanges();
				}

			}
			catch (Exception ex)
			{
				Console.BackgroundColor = ConsoleColor.Red;
				Console.ForegroundColor = ConsoleColor.Gray;
				if (ex.InnerException != null)
				{
					if (ex.InnerException.InnerException != null)
					{
						Console.WriteLine("Error saving group (INNER INNER): {0}",
							ex.InnerException.InnerException.Message);
					}
					else
					{
						Console.WriteLine("Error saving group (INNER): {0}", ex.InnerException.Message);
					}
				}
				else
				{
					Console.WriteLine("Error saving group: {0}", ex.Message);
				}
				Console.ResetColor();
			}
		}

		private static bool ClearOldData()
		{
			try
			{
				using (var context = new lowes_catalogEntities())
				{
					var script = File.ReadAllText("lowes_catalog_drop.sql");
					context.Database.ExecuteSqlCommand(script);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				return false;
			}
			Console.WriteLine("Old data has been dropped.");
			return true;
		}

		private static void CreateSchema()
		{
			try
			{
				var createscript = File.ReadAllText("lowes_catalog.sql");
				using (var context = new lowes_catalogEntities())
				{
					context.Database.ExecuteSqlCommand(createscript);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				throw;
			}
		}
	}
}
