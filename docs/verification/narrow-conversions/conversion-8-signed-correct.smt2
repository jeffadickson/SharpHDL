(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (let ((a!1 (= (ite (bvult (bvand input_0 #x000000ff) #x00000080) #b1 #b0) #b1)))
(let ((a!2 (= ((_ sign_extend 24) ((_ extract 7 0) input_0))
              (ite a!1
                   (bvand input_0 #x000000ff)
                   (bvsub (bvand input_0 #x000000ff) #x00000100)))))
  (not a!2))))

(check-sat)
