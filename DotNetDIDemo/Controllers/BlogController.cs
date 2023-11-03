using DotNetDIDemo.Model;
using DotNetDIDemo.Service;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860
namespace DotNetDIDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BlogController : ControllerBase
    {
        private readonly IBlogRepository _repository;

        public BlogController(IBlogRepository repository)
        {
            _repository = repository;
        }

        // GET: api/<BlogController>
        [HttpGet]
        public ActionResult<IEnumerable<Blog>> Get()
        {
            var blogs = _repository.GetAllBlogs();
            return Ok(blogs);
        }
    }
}