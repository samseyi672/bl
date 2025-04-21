using System;
using System.Collections.Generic;
using System.Text;
//using RazorLight;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using Scriban;
namespace Retailbanking.BL.Services
{

    public class TemplateService
    {
       // private readonly RazorLightEngine _engine;

        public TemplateService(IWebHostEnvironment env)
        {
            /*
            string baseDirectory = env.ContentRootPath;
            // Construct the relative path to the templates folder
            string templatesPath = Path.Combine(baseDirectory, "templates\\layouts\\_Layout.cshtml");
            _engine = new RazorLightEngineBuilder()
                .UseFileSystemProject(templatesPath) // Path to your templates folder
                .UseMemoryCachingProvider()
                .Build();
            */
        }

        public async Task<string> RenderTemplateAsync<T>(string templateName, T model)
        {
            // return await _engine.CompileRenderAsync(templateName, model);
            return null;
        }

        public string RenderScribanTemplate(string templatePath, object data)
        {
            // Read the template file
            string templateContent = File.ReadAllText(templatePath);
            // Parse and render the template
            var template = Template.Parse(templateContent);
            return template.Render(data);
        }

        /*
        public async Task<string> GenerateContent(object model,string TemplatePath, string ModelTemplate)
        {
            var templateService = new TemplateService();
            // var model = new EmailModel { Name = "John Doe" };
            string htmlContent = await templateService.RenderTemplateAsync(ModelTemplate, model);
            return htmlContent;
        }
        */
    }
}

