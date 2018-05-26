#include "ejdb2.h"
#include "jbl.h"
#include <iowow/iwxstr.h>
#include <CUnit/Basic.h>

int init_suite(void) {
  int rc = ejdb2_init();
  return rc;
}

int clean_suite(void) {
  return 0;
}

void jbl_test1_1() {
  const char *data = "{\"foo\": \"bar\", \"num1\":1223, \"num2\":10.123456}";  
  JBL jbl;
  iwrc rc = jbl_from_json(&jbl, data) ;
  CU_ASSERT_EQUAL_FATAL(rc, 0);
  
  IWXSTR *xstr = iwxstr_new();
  CU_ASSERT_PTR_NOT_NULL_FATAL(xstr);
  
  rc = jbl_as_json(jbl, jbl_xstr_json_printer, xstr, false);
  CU_ASSERT_EQUAL_FATAL(rc, 0);
  
  
  iwxstr_destroy(xstr);
  jbl_destroy(&jbl);  
}


int main() {
  CU_pSuite pSuite = NULL;
  if (CUE_SUCCESS != CU_initialize_registry()) return CU_get_error();
  pSuite = CU_add_suite("jbl_test1", init_suite, clean_suite);
  if (NULL == pSuite) {
    CU_cleanup_registry();
    return CU_get_error();
  }
  if (
    (NULL == CU_add_test(pSuite, "jbl_test1_1", jbl_test1_1))
  ) {
    CU_cleanup_registry();
    return CU_get_error();
  }
  CU_basic_set_mode(CU_BRM_VERBOSE);
  CU_basic_run_tests();
  int ret = CU_get_error() || CU_get_number_of_failures();
  CU_cleanup_registry();
  return ret;
}