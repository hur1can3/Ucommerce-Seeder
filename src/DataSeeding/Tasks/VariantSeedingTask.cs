using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFCore.BulkExtensions;
using Ucommerce.Seeder.DataSeeding.Tasks.Cms;
using Ucommerce.Seeder.DataSeeding.Utilities;
using Ucommerce.Seeder.Models;

namespace Ucommerce.Seeder.DataSeeding.Tasks
{
    public class VariantSeedingTask : ProductSeedingTask
    {
        private readonly ICmsContent _cmsContent;

        public VariantSeedingTask(DatabaseSize databaseSize, ICmsContent cmsContent) : base(databaseSize, cmsContent)
        {
            _cmsContent = cmsContent;
        }

        protected override string EntityNamePlural => "variants";

        public override uint Count => _databaseSize.AverageVariantsPerProduct * _databaseSize.Products;

        public class ProductWithDefinition
        {
            public int ProductId { get; set; }
            public int ProductDefinitionId { get; set; }
        }

        public override void Seed(UmbracoDbContext context)
        {
            var languageCodes = _cmsContent.GetLanguageIsoCodes(context);
            var productDefinitionFields = LookupProductDefinitionFields(context, true);
            var priceGroupIds = context.UCommercePriceGroup.Select(pg => pg.PriceGroupId).ToArray();
            
            var productFamilyIds = context.UCommerceProduct
                .Where(p => p.ProductDefinition.UCommerceProductDefinitionField.Any(f => f.IsVariantProperty)) // pick families only
                .Where(p => p.ParentProductId == null) // don't pick variants
                .Select(product => new ProductWithDefinition {ProductId = product.ProductId, ProductDefinitionId = product.ProductDefinitionId})
                .ToArray();
            
            var mediaIds = _cmsContent.GetAllMediaIds(context);
            var contentIds = _cmsContent.GetAllMediaIds(context);

            var products = GenerateVariants(context, productFamilyIds, mediaIds);

            GenerateDescriptions(context, languageCodes, products);

            GenerateProperties(context, products, productDefinitionFields, mediaIds, contentIds);

            GeneratePrices(context, priceGroupIds, products);
        }

        protected IList<UCommerceProduct> GenerateVariants(UmbracoDbContext context, ProductWithDefinition[] products, string[] mediaIds)
        {
            uint batchSize = 100_000;
            uint numberOfBatches = Count / batchSize;
            var inserted = new List<UCommerceProduct>((int)Count);

            Console.Write($"Generating {Count:N0} {EntityNamePlural} in batches of {batchSize:N0}. ");
            
            using (var p = new ProgressBar())
            {
                var variantBatches =
                    GeneratorHelper.Generate(() => GenerateVariant(mediaIds, products),
                        Count).Batch(batchSize);

                variantBatches.EachWithIndex((variants, index) =>
                {
                    var listOfVariants = variants.ToList();
                    context.BulkInsert(listOfVariants, options => options.SetOutputIdentity = true);
                    inserted.AddRange(listOfVariants);
                    p.Report(1.0 * index / numberOfBatches);
                });
                
                return inserted;
            }
        }

        private UCommerceProduct GenerateVariant(string[] mediaIds, IEnumerable<ProductWithDefinition> products)
        {
            var productFamily = _faker.PickRandom(products);
            var product = _productFaker
                .RuleFor(x => x.ParentProductId, f => productFamily.ProductId)
                .RuleFor(x => x.ProductDefinitionId, f => productFamily.ProductDefinitionId)
                .RuleFor(x => x.PrimaryImageMediaId, f => f.PickRandomOrDefault(mediaIds))
                .RuleFor(x => x.ThumbnailImageMediaId, f => f.PickRandomOrDefault(mediaIds))
                .RuleFor(x => x.VariantSku, f => f.Commerce.Ean8())
                .Generate();

            return product;

        }
    }
}