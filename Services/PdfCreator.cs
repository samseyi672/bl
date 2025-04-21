using iText.Html2pdf;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Pdf.Action;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Layout;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using static System.Net.Mime.MediaTypeNames;
using Document = iText.Layout.Document;
using Text = iText.Layout.Element.Text;
using Image = iText.Layout.Element.Image;
using iText.Kernel.Geom;
using System.Reflection.Metadata.Ecma335;

namespace Retailbanking.BL.Services
{
    public class PageFooterEventHandler : IEventHandler
    {
        protected int totalPages;
        public PageFooterEventHandler(int totalPages)
        {
            this.totalPages = totalPages;
        }

        public void HandleEvent(Event @event)
        {
            PdfDocumentEvent docEvent = (PdfDocumentEvent)@event;
            PdfDocument pdf = docEvent.GetDocument();
            PdfPage page = docEvent.GetPage();
            int pageNumber = pdf.GetPageNumber(page);
            Rectangle pageSize = page.GetPageSize();
            PdfCanvas pdfCanvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), pdf);
            Canvas canvas = new Canvas(pdfCanvas, pageSize);
            canvas.ShowTextAligned(new Paragraph(String.Format("page " + pageNumber + " of " + totalPages)),
                pageSize.GetWidth() - 36, pageSize.GetTop() - 20, TextAlignment.RIGHT);
        }
    }

    public class PdfCreator
    {
        private string fileName { get; set; }

        public PdfCreator(string fileName)
        {
            this.fileName = fileName;
        }


        public void MyThreadMethod(IGeneric genServ, SendMailObject sendMailObject, MemoryStream pdfStream)
        {
            Console.WriteLine("Thread started");
            // genServ.SendMail(sendMailObject);
            genServ.SendMail(sendMailObject, pdfStream);
            genServ.LogRequestResponse("mail sent in thread ", $"", "");
            // Thread code goes here
        }

        public void createPdfStatement(List<string> listofdata, List<string> lisofColumns, string StartDate, string EndDate, IGeneric _genServ)
        {
            Console.WriteLine("enter in pdf creator ...");
            FileInfo file = new FileInfo(fileName);
            var stream = new FileStream(fileName, FileMode.Create, access: FileAccess.ReadWrite);
            _genServ.LogRequestResponse("setting the stream", $"", "");
            PdfWriter writer = new PdfWriter(stream);
            PdfDocument pdf = new PdfDocument(writer);
            Document document = new Document(pdf);
            // Header
            Paragraph header = new Paragraph("ACCOUNT STATEMENT")
               .SetTextAlignment(TextAlignment.CENTER)
               .SetFontSize(20);
            // New line
            Paragraph newline = new Paragraph(new Text("\n"));
            document.Add(newline);
            document.Add(header);
            // Add sub-header
            Paragraph subheader = new Paragraph(fileName)
               .SetTextAlignment(TextAlignment.CENTER)
               .SetFontSize(15);
            document.Add(subheader);
            // Line separator
            LineSeparator ls = new LineSeparator(new SolidLine());
            document.Add(ls);
            // Add paragraph1
            Paragraph paragraph1 = new Paragraph("Account Statement from " + StartDate + " to " + EndDate);
            paragraph1.SetTextAlignment(TextAlignment.CENTER);
            document.Add(paragraph1);
            // Add image
            Image img = new Image(ImageDataFactory
               .Create(@".\wwwroot\image.png"))
               .SetTextAlignment(TextAlignment.CENTER);
            img.SetHeight(100);
            img.SetWidth(100);
            document.Add(img);
            Console.WriteLine("document created with header ...");
            Table table = new Table(lisofColumns.Count, false);
            for (int i = 0; i < listofdata.Count; i++)  // setting  columns
            {
                Cell cell11 = new Cell(1, 1)
               .SetBackgroundColor(ColorConstants.GRAY)
               .SetTextAlignment(TextAlignment.CENTER)
               .Add(new Paragraph(lisofColumns.ElementAtOrDefault(i)));
                table.AddCell(cell11);
            }
            // add the values        
            for (int i = 0; i < listofdata.Count; i++)
            {
                Console.WriteLine("count " + i);
                string element = listofdata.ElementAtOrDefault(i) != null ? listofdata.ElementAtOrDefault(i) : " ";
                Console.WriteLine("element " + element);
                Cell cell11 = new Cell(1, 1)
              .SetBackgroundColor(ColorConstants.GRAY)
              .SetTextAlignment(TextAlignment.CENTER)
              .Add(new Paragraph(element));
                table.AddCell(cell11);
                cell11 = null;
            }
            document.Add(newline);
            document.Add(table);
            Console.WriteLine("table added ......");
            _genServ.LogRequestResponse("table added ", $"", "");
            // Hyper link
            Link link = new Link("Visit our Websites",
               PdfAction.CreateURI("https://trustbancgroup.com"));
            Paragraph hyperLink = new Paragraph("Please ")
               .Add(link.SetBold().SetUnderline()
               .SetItalic().SetFontColor(ColorConstants.BLUE))
               .Add("TrustBanc Financial Group Limited");
            document.Add(newline);
            document.Add(hyperLink);
            // Page numbers
            int n = pdf.GetNumberOfPages();
            for (int i = 1; i <= n; i++)
            {
                document.ShowTextAligned(new Paragraph(String
                   .Format("page" + i + " of " + n)),
                   559, 806, i, TextAlignment.RIGHT,
                   VerticalAlignment.TOP, 0);
            }
            _genServ.LogRequestResponse("saving the document ", $"", "");
            // Close document
            document.Close();
        }
        private void createpdfreceipt(TransactionListRequest transactionListRequest)
        {
            string htmlContent = @"
                <html>
                <head>
                <title>Test HTML to PDF</title>
                </head>
                <body>
                <h1>Hello, World!</h1>
                <p>This is a test of converting HTML to PDF.</p>
                </body>
                </html>";
            // Output file path
            string outputFilePath = "receitp.pdf";
            // HTMLWorker
            var stream = new FileStream(fileName, FileMode.Create, access: FileAccess.ReadWrite);
            // Convert HTML to PDF
            HtmlConverter.ConvertToPdf(htmlContent, stream);
        }
        public static byte[] GeneratePdf(string htmlContent)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                ConverterProperties properties = new ConverterProperties();
                Console.WriteLine(htmlContent);
                HtmlConverter.ConvertToPdf(htmlContent, memoryStream, properties);
                Console.WriteLine("stream created");
                return memoryStream.ToArray();
            }
        }
        public static MemoryStream CreatePdfInMemory(List<TransactionData> listOfData, List<string> listOfColumns, string filename, string StartDate, string EndDate, IGeneric _genServ)
        {
            // Create a MemoryStream to hold the PDF data
            MemoryStream pdfMemoryStream = new MemoryStream();
            // Initialize PdfWriter with the MemoryStream
            PdfWriter writer = new PdfWriter(pdfMemoryStream);
            PdfDocument pdf = new PdfDocument(writer);
            Document document = new Document(pdf);
            //  using (PdfWriter writer = new PdfWriter(pdfMemoryStream))
            //{
            // Set the writer to immediate flush mode to write directly to the MemoryStream
            writer.SetCloseStream(false);
            // Initialize PdfDocument with the writer
            //  using (PdfDocument pdf = new PdfDocument(writer))
            //  {
            // Initialize Document with the PdfDocument
            //using (Document document = new Document(pdf))
            // {
            // Your existing setup code for document (header, subheader, image, etc.)
            // Creating the table with the number of columns based on listOfColumns
            // Header
            Paragraph header = new Paragraph("ACCOUNT STATEMENT")
               .SetTextAlignment(TextAlignment.CENTER)
               .SetFontSize(20);
            // New line
            Paragraph newline = new Paragraph(new Text("\n"));
            document.Add(newline);
            document.Add(header);
            // Add sub-header
            Paragraph subheader = new Paragraph(filename)
               .SetTextAlignment(TextAlignment.CENTER)
               .SetFontSize(15);
            document.Add(subheader);
            // Line separator
            LineSeparator ls = new LineSeparator(new SolidLine());
            document.Add(ls);
            // Add paragraph1
            Paragraph paragraph1 = new Paragraph("Account Statement from " + StartDate + " to " + EndDate);
            paragraph1.SetTextAlignment(TextAlignment.CENTER);
            document.Add(paragraph1);
            // Add image
            Image img = new Image(ImageDataFactory
               .Create(@".\wwwroot\image.png"))
               .SetTextAlignment(TextAlignment.CENTER);
            img.SetHeight(100);
            img.SetWidth(100);
            document.Add(img);
            Console.WriteLine("document created with header ...");
            CultureInfo nigerianCulture = new CultureInfo("en-NG");
            Table table = new Table(UnitValue.CreatePercentArray(listOfColumns.Count)).UseAllAvailableWidth();
            //  Table table = new Table(listOfColumns.Count, false);
            // Adding column headers to the table
            foreach (var column in listOfColumns)
            {
                table.AddHeaderCell(new Cell().Add(new Paragraph(column)).SetBackgroundColor(ColorConstants.GRAY).SetTextAlignment(TextAlignment.CENTER));
            }
            // Adding rows of transaction data to the table
            Console.WriteLine("header added proceeding to add rows");
            int p = 0;
            foreach (var transaction in listOfData)
            {
                table.AddCell(new Cell().Add(new Paragraph(transaction.AccountNumber == null ? "" : transaction.AccountNumber)));
                table.AddCell(new Cell().Add(new Paragraph(transaction.TranDate == null ? "" : transaction.TranDate)));
                table.AddCell(new Cell().Add(new Paragraph(transaction.Narration == null ? "" : transaction.Narration)));
                table.AddCell(new Cell().Add(new Paragraph(transaction.Debit.ToString("F2", nigerianCulture))));
                table.AddCell(new Cell().Add(new Paragraph(transaction.Credit.ToString("F2", nigerianCulture))));
                table.AddCell(new Cell().Add(new Paragraph(transaction.Balance.ToString("F2", nigerianCulture))));
                table.AddCell(new Cell().Add(new Paragraph(transaction.BranchName)));
            }
            document.Add(newline);
            document.Add(table);
            Console.WriteLine("all rows added ...........");
            _genServ.LogRequestResponse("table added ", $"", "");
            // Hyper link
            Link link = new Link("Visit our websites",
               PdfAction.CreateURI("https://trustbancmfb.com/"));
            Paragraph hyperLink = new Paragraph("Please ")
               .Add(link.SetBold().SetUnderline()
               .SetItalic().SetFontColor(ColorConstants.BLUE))
               .Add("TrustBanc Financial Group Limited");
            document.Add(newline);
            document.Add(hyperLink);
            // Page numbers
            int n = pdf.GetNumberOfPages();
            for (int i = 1; i <= n; i++)
            {
                document.ShowTextAligned(new Paragraph(String
                   .Format("page" + i + " of " + n)),
                   559, 806, i, TextAlignment.RIGHT,
                   VerticalAlignment.TOP, 0);
            }
            // Your existing code for adding the table, hyperlink, and page numbers to the document
            // Close document
            document.Close();
            // }
            //}
            // Important: Reset the MemoryStream position to the beginning of the stream
            pdfMemoryStream.Position = 0;
            // Return the MemoryStream containing the PDF
            return pdfMemoryStream;
        }
        //}
        public static MemoryStream CreatePdfInMemory(List<string> listofdata, List<string> lisofColumns, string filename, string StartDate, string EndDate, IGeneric _genServ)
        {
            // Create a MemoryStream to hold the PDF data
            MemoryStream pdfMemoryStream = new MemoryStream();

            // Initialize PdfWriter with the MemoryStream
            PdfWriter writer = new PdfWriter(pdfMemoryStream);

            // Set the writer to immediate flush mode to write directly to the MemoryStream
            writer.SetCloseStream(false);

            // Initialize PdfDocument with the writer
            PdfDocument pdf = new PdfDocument(writer);

            // Initialize Document with the PdfDocument
            Document document = new Document(pdf);
            // Header
            Paragraph header = new Paragraph("ACCOUNT STATEMENT")
               .SetTextAlignment(TextAlignment.CENTER)
               .SetFontSize(20);
            // New line
            Paragraph newline = new Paragraph(new Text("\n"));
            document.Add(newline);
            document.Add(header);
            // Add sub-header
            Paragraph subheader = new Paragraph(filename)
               .SetTextAlignment(TextAlignment.CENTER)
               .SetFontSize(15);
            document.Add(subheader);
            // Line separator
            LineSeparator ls = new LineSeparator(new SolidLine());
            document.Add(ls);
            // Add paragraph1
            Paragraph paragraph1 = new Paragraph("Account Statement from " + StartDate + " to " + EndDate);
            paragraph1.SetTextAlignment(TextAlignment.CENTER);
            document.Add(paragraph1);
            // Add image
            Image img = new Image(ImageDataFactory
               .Create(@".\wwwroot\image.png"))
               .SetTextAlignment(TextAlignment.CENTER);
            img.SetHeight(100);
            img.SetWidth(100);
            document.Add(img);
            Console.WriteLine("document created with header ...");
            Table table = new Table(lisofColumns.Count, false);
            for (int i = 0; i < lisofColumns.Count; i++)  // setting  columns
            {
                Cell cell11 = new Cell(1, 1)
               .SetBackgroundColor(ColorConstants.GRAY)
               .SetTextAlignment(TextAlignment.CENTER)
               .Add(new Paragraph(lisofColumns.ElementAtOrDefault(i)));
                table.AddCell(cell11);
            }
            Console.WriteLine("table header created. adding the value ...");
            // add the values        
            for (int i = 0; i < listofdata.Count; i++)
            {
                string element = listofdata.ElementAtOrDefault(i) != null ? listofdata.ElementAtOrDefault(i) : " ";
                Console.WriteLine("element " + element);
                Cell cell11 = new Cell(1, 1)
              .SetBackgroundColor(ColorConstants.GRAY)
              .SetTextAlignment(TextAlignment.CENTER)
              .Add(new Paragraph(element));
                table.AddCell(cell11);
                cell11 = null;
            }
            document.Add(newline);
            document.Add(table);
            Console.WriteLine("table added ......");
            _genServ.LogRequestResponse("table added ", $"", "");
            // Hyper link
            Link link = new Link("Visit our websites",
               PdfAction.CreateURI("https://trustbancmfb.com/"));
            Paragraph hyperLink = new Paragraph("Please ")
               .Add(link.SetBold().SetUnderline()
               .SetItalic().SetFontColor(ColorConstants.BLUE))
               .Add("TrustBanc Financial Group Limited");
            document.Add(newline);
            document.Add(hyperLink);
            // Page numbers
            int n = pdf.GetNumberOfPages();
            for (int i = 1; i <= n; i++)
            {
                document.ShowTextAligned(new Paragraph(String
                   .Format("page" + i + " of " + n)),
                   559, 806, i, TextAlignment.RIGHT,
                   VerticalAlignment.TOP, 0);
            }
            _genServ.LogRequestResponse("saving the document ", $"", "");
            // Close document
            document.Close();

            // Important: Reset the MemoryStream position to the beginning of the stream
            pdfMemoryStream.Position = 0;

            // Return the MemoryStream containing the PDF
            return pdfMemoryStream;
        }
        private static string maskedAccountNumber(string originalNumber) {
            int maskLength = originalNumber.Length - 5; // Calculate the length to mask, excluding the first 2 and last 3 characters

            string maskedNumber = originalNumber.Substring(0, 2) // Take the first 2 characters
                                   + new string('*', maskLength) // Create a string of asterisks of the desired length
                                   + originalNumber.Substring(originalNumber.Length - 3); // Take the last 3 characters

            Console.WriteLine("maskedNumber " + maskedNumber);
            return maskedNumber;
        }
        private static TimeSpan parseDateTime(String dateTimeString,IGeneric generic) {
            // string dateTimeString = "1/1/0001 12:00:00 AM";
            // Define possible date formats
            TimeSpan timeSpan;
            string[] formats = { "M/d/yyyy h:mm:ss tt", "MM/dd/yyyy HH:mm:ss", "M/d/yyyy H:mm:ss", "MM/dd/yyyy H:mm:ss" };
            DateTime dateTime;
            bool success = DateTime.TryParseExact(
                dateTimeString,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateTime
            );
            if (success)
            {
                // Getting the time component as TimeSpan
                timeSpan = dateTime.TimeOfDay;
                // Output for verification
                generic.LogRequestResponse("Parsed DateTime: " + dateTime,"","");
                generic.LogRequestResponse("Time Component as TimeSpan: " + timeSpan, "", "");
            }
            else
            {
                Console.WriteLine("Failed to parse the date time string.");
                // Handle failure, perhaps by assigning a default value
                dateTime = DateTime.MinValue;
                 timeSpan = dateTime.TimeOfDay;
            }
            return timeSpan;
        }
        public static string ReceiptHtml(string image, TransactionListRequest transactionListRequest,FinedgeSearchBvn finedgeSearchBvn,string AvailableBalance,IGeneric _generv,string type=null)
        {       
            string dateTimeString = transactionListRequest.CreatedOn;
            TimeSpan timeSpan = parseDateTime(dateTimeString,_generv);
            // return $"<p>Test email {timeSpan}</p>"; 
            return returnHtml(timeSpan,image,transactionListRequest,finedgeSearchBvn,AvailableBalance); 
        }
        private static string returnHtml(TimeSpan timeSpan,string image, TransactionListRequest transactionListRequest, FinedgeSearchBvn finedgeSearchBvn, string AvailableBalance,string type=null)
        {
            return $@"
               <!DOCTYPE html>
       <html lang=""en"">
          <head>
       <meta charset=""UTF-8"" />
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>Transaction Notification</title>
    <link rel=""stylesheet"" href=""https://use.typekit.net/oov2wcw.css"" />
    <style>
      body {{
        font-family: century-gothic, sans-serif;
        font-weight: 400;
        font-style: normal;
        display: flex;
        justify-content: center;
        align-items: center;
        flex-direction: column;
        background-color: #fff;
        line-height: 25px;
      }}
      .container {{
        max-width: 100%;
      }}
      h1,
      h2,
      h3,
      h4 {{
        color: #044083;
      }}

      h1 {{
        font-size: 32px;
      }}

      h2 {{
        font-size: 26px;
      }}

      h3 {{
        font-size: 22px;
      }}

      h4 {{
        font-size: 20px;
      }}

      h5 {{
        font-size: 14px;
      }}

      p {{
        font-size: 14px;
      }}

      /* table-center {{
        display: flex;
        justify-content: center;
      }} */

      table {{
        font-size: 12px;
        border: 1px solid #000;
        min-width: 100%;
      }}

      .table-head {{
        text-align: center;
        background-color: #cae4f1;
      }}

      .table-head-text {{
        color: #044083;
      }}

      .head-section {{
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding: 5px;
        margin-bottom: 10px;
      }}

      .body-section {{
        padding: 0 5px;
      }}

      .footer-section {{
        padding: 0 5px;
        text-align: left;
      }}

      .social-icons {{
        display: flex;
        justify-content: center;
      }}
      .social-icons img {{
        width: 20px;
        padding: 3px;
      }}

      td {{
        font-size: 16px;
      }}

      @media only screen and (max-width: 600px) {{
        body {{
          line-height: 17px;
        }}

        container {{
          max-width: 100%;
        }}

        .box1 {{
          width: 70%;
        }}

        .box2 {{
          width: 30%;
        }}

        table {{
          min-width: 100%;
        }}

        h1 {{
          font-size: 16px;
          font-weight: 900;
        }}

        h3 {{
          font-size: 14px;
        }}

        h4 {{
          font-size: 12px;
        }}

        p {{
          font-size: 14px;
        }}

        .pin-image {{
          width: 200px;
        }}

        .logo-image {{
          width: 100px;
          margin-bottom: 5px;
        }}

        .footer-section p {{
          font-size: 9px;
        }}

      }}
    </style>
  </head>
  <body>
    <container class=""container"">
      <div class=""head-image"">
        <img src="""" alt=""header"" width=""100%"" />
      </div>
      <div class=""head-section"">
        <div class=""box1"">
          <h1>Transaction Notification</h1>
        </div>
      </div>

      <div class=""body-section"">
        <h3>Dear {finedgeSearchBvn.result.firstname.ToUpper()} {finedgeSearchBvn.result.firstname.ToUpper()} {finedgeSearchBvn.result.firstname.ToUpper()},</h3>
        <p>
          Below are details of a transaction made from your account today:
          The Time is {timeSpan.ToString("G")}.
        </p>
        <div class=""table-center"">
          <table style=""margin-left: auto; margin-right: auto;"">
            <tr class=""table-head"">
              <td colspan=""2"" class=""table-head-text"">
                <h5>TRANSACTION NOTIFICATION (MOBILE BANKING)</h5>
              </td>
            </tr>
            <tr>
              <td>Beneficiary Name:</td>
              <td>{transactionListRequest.Destination_AccountName}</td>
            </tr>
            <tr>
              <td>Effective Date:</td>
              <td>{transactionListRequest.CreatedOn}</td>
            </tr>
            <tr>
              <td>Currency:</td>
              <td>NGN</td>
            </tr>
            <tr>
              <td>Narration:</td>
              <td>{transactionListRequest.Narration}</td>
            </tr>
            <tr>
              <td>Account Number:</td>
              <td>{(maskedAccountNumber(transactionListRequest.Source_Account))}</td>
            </tr>
            <tr>
              <td>Credit Account:</td>
              <td>{(maskedAccountNumber(transactionListRequest.Destination_Account))}</td>
            </tr>
            <tr>
              <td>Description:</td>
              <td>{transactionListRequest.Destination_BankName}</td>
            </tr>
            <tr>
              <td>Transaction Type:</td>
              <td>{type}</td>
            </tr>
            <tr>
              <td>AmountOrPrincipal:</td>
              <td>{transactionListRequest.Amount}</td>
            </tr>
              <tr>
              <td>Reference Code:</td>
              <td>{transactionListRequest.TransID}</td>
            </tr>
             <tr>
              <td>Available Balance:</td>
              <td>{decimal.Parse(AvailableBalance).ToString("F2",new CultureInfo("en-NG"))}</td>
            </tr>
          </table>
        </div>
      </div>
      <div class=""footer-section"">
        <p className=""footer-text"">
          Remember: Keep your card and PIN information secure. Do not respond to
          emails requesting for your card/ PIN details. If you think an email is
          suspicious, don't click on any links.
        </p>
        <p className=""footer-text"">
          Instead, forward it to: it@trustbancgroup.com and delete
        </p>
      </div>
      <div class=""social-icons"">
        <img src=""images/Facebook.png"" alt=""facebook"" />
        <img src=""images/Instagram.png"" alt=""instagram"" />
        <img src=""images/Twitter.png"" alt=""twitter"" />
        <img src=""images/Linkedin.png"" alt=""linkedin"" />
      </div>
    </container>
  </body>
</html>
                 ";
        }
        public static string ReceiptHtml(string filename, string image, TransactionListRequest transactionListRequest, IGeneric _generv)
        {
            string dateTimeString = transactionListRequest.CreatedOn;
            // DateTime.ParseExact(Request.StartDate, "MM-dd-yyyy", CultureInfo.InvariantCulture)
            //       .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateTime dateTime = DateTime.ParseExact(transactionListRequest.CreatedOn, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            // Getting the time component as TimeSpan
            TimeSpan timeSpan = dateTime.TimeOfDay;
            _generv.LogRequestResponse("timespan ", $"{timeSpan.ToString("g")}", "");
            return $@"
               <!DOCTYPE html>
<html lang=""en"">
  <head>
    <meta charset=""UTF-8"" />
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>Transaction Receipt</title>
    <link rel=""stylesheet"" href=""https://use.typekit.net/oov2wcw.css"" />
    <style>
      body {{
        font-family: century-gothic, sans-serif;
        font-weight: 400;
        font-style: normal;
        display: flex;
        justify-content: center;
        align-items: center;
        flex-direction: column;
        background-color: #fff;
        line-height: 25px;
      }}
      .container {{
        max-width: 100%;
      }}
      h1,
      h2,
      h3,
      h4 {{
        color: #044083;
      }}

      h1 {{
        font-size: 32px;
      }}

      h2 {{
        font-size: 26px;
      }}

      h3 {{
        font-size: 22px;
      }}

      h4 {{
        font-size: 20px;
      }}

      h5 {{
        font-size: 14px;
      }}

      p {{
        font-size: 14px;
      }}

      /* table-center {{
        display: flex;
        justify-content: center;
      }} */

      table {{
        font-size: 12px;
        border: 1px solid #000;
        min-width: 100%;
      }}

      .table-head {{
        text-align: center;
        background-color: #cae4f1;
      }}

      .table-head-text {{
        color: #044083;
      }}

      .head-section {{
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding: 5px;
        margin-bottom: 10px;
      }}

      .body-section {{
        padding: 0 5px;
      }}

      .footer-section {{
        padding: 0 5px;
        text-align: left;
      }}

      .social-icons {{
        display: flex;
        justify-content: center;
      }}
      .social-icons img {{
        width: 20px;
        padding: 3px;
      }}

      td {{
        font-size: 16px;
      }}

      @media only screen and (max-width: 600px) {{
        body {{
          line-height: 17px;
        }}

        container {{
          max-width: 100%;
        }}

        .box1 {{
          width: 70%;
        }}

        .box2 {{
          width: 30%;
        }}

        table {{
          min-width: 100%;
        }}

        h1 {{
          font-size: 16px;
          font-weight: 900;
        }}

        h3 {{
          font-size: 14px;
        }}

        h4 {{
          font-size: 12px;
        }}

        p {{
          font-size: 14px;
        }}

        .pin-image {{
          width: 200px;
        }}

        .logo-image {{
          width: 100px;
          margin-bottom: 5px;
        }}

        .footer-section p {{
          font-size: 9px;
        }}

      }}
    </style>
  </head>
  <body>
    <container class=""container"">
      <div class=""head-image"">
        <img src=""{image}"" alt=""header"" width=""100%"" />
      </div>
      <div class=""head-section"">
        <div class=""box1"">
          <h1>Transaction Receipt</h1>
        </div>
      </div>

      <div class=""body-section"">
        <h3>Dear {transactionListRequest.Destination_AccountName},</h3>
        <p>
          Below are details of a transaction made from your iucormeteraccount today:
          The Time is {timeSpan.ToString("G")}.
        </p>
        <div class=""table-center"">
          <table style=""margin-left: auto; margin-right: auto;"">
            <tr class=""table-head"">
              <td colspan=""2"" class=""table-head-text"">
                <h5>TRANSACTION RECEIPT (MOBILE BANKING)</h5>
              </td>
            </tr>
            <tr>
              <td>Beneficiary Name:</td>
              <td>{transactionListRequest.Destination_AccountName}</td>
            </tr>
            <tr>
              <td>Effective Date:</td>
              <td>{transactionListRequest.CreatedOn}</td>
            </tr>
            <tr>
              <td>Currency:</td>
              <td>NGN</td>
            </tr>
            <tr>
              <td>Description:</td>
              <td>{transactionListRequest.Narration}</td>
            </tr>
            <tr>
              <td>Reference Code:</td>
              <td>{transactionListRequest.TransID}</td>
            </tr>
            <tr>
              <td>Account Number:</td>
              <td>{(maskedAccountNumber(transactionListRequest.Source_Account))}</td>
            </tr>
            <tr>
              <td>Credit Account:</td>
              <td>{(maskedAccountNumber(transactionListRequest.Destination_Account))}</td>
            </tr>
            <tr>
              <td>Bank:</td>
              <td>{transactionListRequest.Destination_BankName}</td>
            </tr>
            <tr>
              <td>Transaction Type:</td>
              <td>DEBIT</td>
            </tr>
            <tr>
              <td>Date of Transaction</td>
              <td>{transactionListRequest.CreatedOn}</td>
            </tr>
            <tr>
              <td>AmountOrPrincipal:</td>
              <td>{transactionListRequest.Amount}</td>
            </tr>
          </table>
        </div>
      </div>
      <div class=""footer-section"">
        <p className=""footer-text"">
          Remember: Keep your card and PIN information secure. Do not respond to
          emails requesting for your card/ PIN details. If you think an email is
          suspicious, don't click on any links.
        </p>
        <p className=""footer-text"">
          Instead, forward it to: it@trustbancgroup.com and delete
        </p>
      </div>
      <div class=""social-icons"">
        <img src=""images/Facebook.png"" alt=""facebook"" />
        <img src=""images/Instagram.png"" alt=""instagram"" />
        <img src=""images/Twitter.png"" alt=""twitter"" />
        <img src=""images/Linkedin.png"" alt=""linkedin"" />
      </div>
    </container>
  </body>
</html>
   ";
        }

    }

}



