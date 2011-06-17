﻿using EPiServer.DataAbstraction;
using PageTypeBuilder.Abstractions;
using PageTypeBuilder.Helpers;

namespace PageTypeBuilder.Migrations
{
    public class PageTypeAction
    {
        IPageType pageType;
        IMigrationContext context;

        public PageTypeAction(
            IPageType pageType,
            IMigrationContext context)
        {
            this.pageType = pageType;
            this.context = context;
        }

        public void Delete()
        {
            if(pageType.IsNull())
            {
                return;
            }

            context.PageTypeRepository.Delete(pageType);
        }

        public void Rename(string newName)
        {
            if (pageType.IsNull())
            {
                return;
            }

            pageType.Name = newName;
            context.PageTypeRepository.Save(pageType);
        }

        public PageDefinitionAction PageDefinition(string name)
        {
            PageDefinition pageDefinition = null;
            if(pageType.IsNotNull())
            {
                pageDefinition = pageType.Definitions.Find(d => d.Name == name);    
            }

            return new PageDefinitionAction(pageDefinition, context);
        }
    }
}